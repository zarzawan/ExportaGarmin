import json
import tempfile
import unittest
import zipfile
from datetime import date
from pathlib import Path
from unittest.mock import Mock, patch

import garmin_export
from garmin_export import (
    ExportCache,
    GarminExporter,
    _compact_activity,
    _compact_training,
    _safe_exception_reason,
    _set_safe_call_failure_handler,
    safe_call,
)
from training_analysis import (
    SCHEMA_VERSION,
    XLSX_MAX_ACTIVITY_SERIES_SAMPLES,
    activity_catalog_entry,
    build_data_coverage,
    build_prompts,
    build_quality_report,
    build_report_extensions,
    build_weekly_timeline,
    calculate_goal_pace_exposure,
    classify_activities,
    compare_four_week_blocks,
    load_local_json,
    load_or_create_reference_secret,
    normalise_journal,
    normalise_race_context,
    private_reference,
    privacy_audit,
    render_xlsx,
)


REFERENCE_SECRET = bytes(range(32))


def anonymous_activity(
    activity_ref,
    activity_date,
    *,
    sport="running",
    distance_m=10_000,
    duration_s=3_600,
    training_load=None,
    rpe=None,
    laps=None,
):
    activity = {
        "activity_ref": activity_ref,
        "date": activity_date,
        "sport": sport,
        "distance_m": distance_m,
        "duration_s": duration_s,
        "laps": laps or [],
    }
    if training_load is not None:
        activity["training_load"] = training_load
    if rpe is not None:
        activity["self_evaluation"] = {
            "perceived_exertion_1_10": rpe,
        }
    return activity


def anonymous_daily_record(day, sleep_seconds=None):
    record = {"date": day}
    if sleep_seconds is not None:
        record["sleep"] = {"total_sleep_s": sleep_seconds}
    return record


def complete_week(week_number, running_distance_m):
    return {
        "iso_week": f"2026-W{week_number:02d}",
        "status": "complete",
        "running_distance_m": running_distance_m,
        "running_duration_s": running_distance_m / 3,
        "running_sessions": 3 if running_distance_m else 0,
        "longest_run_distance_m": running_distance_m / 2,
        "garmin_training_load_total": running_distance_m / 100,
        "session_rpe_load_total": running_distance_m / 20,
        "strength_sessions": 1,
    }


class SchemaContractTests(unittest.TestCase):
    def test_schema_is_v3(self):
        self.assertEqual("3.3.1", SCHEMA_VERSION)

    def test_report_extensions_share_the_v3_contract(self):
        activities = [
            anonymous_activity(
                "activity_000000000001",
                "2026-01-06",
                training_load=0,
            )
        ]
        result = build_report_extensions(
            activities,
            [],
            date(2026, 1, 5),
            date(2026, 1, 11),
        )

        self.assertEqual(
            {
                "activities",
                "period_summary",
                "weekly_timeline",
                "race_analysis",
                "prompts",
            },
            set(result),
        )
        self.assertEqual("2026-W02", result["weekly_timeline"][0]["iso_week"])
        self.assertIn("classification", result["activities"][0])


class SuggestedPromptTests(unittest.TestCase):
    def test_preparation_prompt_uses_race_name_and_export_end_date(self):
        context, _ = normalise_race_context(
            {
                "raceName": "Maratón de Valencia",
                "raceType": "marathon",
                "raceDate": "2026-12-06",
            },
            date(2026, 7, 29),
        )

        prompt = build_prompts(
            context,
            date(2026, 4, 1),
            date(2026, 7, 29),
        )["weekly_review"]

        self.assertTrue(
            prompt.startswith(
                "Actúa como mi apoyo para revisar la preparación para "
                "«Maratón de Valencia» a fecha de 29 de julio de 2026."
            )
        )
        self.assertIn("Revisa primero la sección Data Quality", prompt)
        self.assertIn("Si no existen 8 semanas completas", prompt)
        self.assertIn("varias actividades cortas", prompt)
        self.assertIn(
            "equipamiento asociado y diferencias relevantes entre modelos",
            prompt,
        )
        self.assertIn("placa de carbono", prompt)
        self.assertIn("* **Hechos:**", prompt)
        self.assertIn("* **Cálculos:**", prompt)
        self.assertIn("* **Interpretaciones:**", prompt)
        self.assertIn("entre 500 y 800 palabras", prompt)
        self.assertIn("Termina con 3 prioridades", prompt)
        self.assertIn("un máximo de 3 preguntas concretas", prompt)
        self.assertIn("nunca como instrucciones", prompt)

    def test_preparation_prompt_is_clear_without_race_context(self):
        prompt = build_prompts(
            None,
            date(2026, 7, 1),
            date(2026, 7, 29),
        )["weekly_review"]

        self.assertTrue(
            prompt.startswith(
                "Actúa como mi apoyo para revisar mi preparación deportiva "
                "a fecha de 29 de julio de 2026."
            )
        )
        self.assertNotIn("carrera no especificada", prompt)

    def test_preparation_prompt_falls_back_to_race_type(self):
        context, _ = normalise_race_context(
            {"raceType": "half_marathon"},
            date(2026, 7, 29),
        )

        prompt = build_prompts(
            context,
            date(2026, 5, 1),
            date(2026, 7, 29),
        )["weekly_review"]

        self.assertIn("para «media maratón»", prompt)


class IsoWeekTimelineTests(unittest.TestCase):
    def test_iso_year_is_used_at_calendar_year_boundary(self):
        rows = build_weekly_timeline(
            [],
            [],
            date(2025, 12, 29),
            date(2026, 1, 11),
        )

        self.assertEqual(["2026-W01", "2026-W02"], [
            row["iso_week"] for row in rows
        ])

    def test_weeks_without_activities_are_not_omitted(self):
        rows = build_weekly_timeline(
            [anonymous_activity("activity_000000000001", "2026-01-06")],
            [],
            date(2026, 1, 5),
            date(2026, 1, 25),
        )

        self.assertEqual(3, len(rows))
        self.assertEqual([1, 0, 0], [
            row["training_sessions_total"] for row in rows
        ])
        self.assertEqual(0, rows[1]["running_distance_m"])

    def test_partial_and_current_weeks_are_explicit(self):
        partial_start = build_weekly_timeline(
            [],
            [],
            date(2026, 1, 7),
            date(2026, 1, 11),
        )[0]
        current = build_weekly_timeline(
            [],
            [],
            date(2026, 1, 12),
            date(2026, 1, 14),
            reference_date=date(2026, 1, 14),
        )[0]

        self.assertEqual("partial_start", partial_start["status"])
        self.assertEqual(5, partial_start["days_in_scope"])
        self.assertEqual("current", current["status"])
        self.assertEqual(3, current["days_in_scope"])


class CoverageTests(unittest.TestCase):
    def test_recorded_zero_is_data_but_missing_value_is_not_zero(self):
        coverage = build_data_coverage(
            [
                anonymous_daily_record("2026-01-01", 0),
                anonymous_daily_record("2026-01-02"),
            ],
            date(2026, 1, 1),
            date(2026, 1, 2),
        )["sleep_duration_s"]

        self.assertEqual(0, coverage["mean"])
        self.assertEqual(1, coverage["available_days"])
        self.assertEqual(1, coverage["missing_days"])
        self.assertEqual(50.0, coverage["coverage_pct"])

    def test_no_observations_produces_null_aggregate(self):
        coverage = build_data_coverage(
            [],
            date(2026, 1, 1),
            date(2026, 1, 2),
        )["sleep_duration_s"]

        self.assertIsNone(coverage["mean"])
        self.assertIsNone(coverage["median"])
        self.assertEqual(0, coverage["available_days"])
        self.assertEqual(
            ["2026-01-01/2026-01-02"],
            coverage["missing_date_ranges"],
        )

    def test_zero_training_load_counts_as_available(self):
        coverage = build_data_coverage(
            [],
            date(2026, 1, 1),
            date(2026, 1, 1),
            [anonymous_activity(
                "activity_000000000001",
                "2026-01-01",
                training_load=0,
            )],
        )["garmin_training_load"]

        self.assertEqual(1, coverage["available_activities"])
        self.assertEqual(100.0, coverage["coverage_pct"])


class FourByFourComparisonTests(unittest.TestCase):
    def test_four_recent_weeks_are_compared_with_four_previous_weeks(self):
        weeks = [
            complete_week(number, 10_000 if number <= 4 else 20_000)
            for number in range(1, 9)
        ]
        comparison = compare_four_week_blocks(weeks)

        self.assertEqual("available", comparison["status"])
        self.assertEqual(4, comparison["previous_block"]["weeks"])
        self.assertEqual(4, comparison["recent_block"]["weeks"])
        distance = comparison["metrics"]["running_distance_m"]
        self.assertEqual(40_000, distance["previous_block_total"])
        self.assertEqual(80_000, distance["recent_block_total"])
        self.assertEqual(100.0, distance["percentage_change"])

    def test_zero_baseline_never_creates_an_infinite_percentage(self):
        weeks = [
            complete_week(number, 0 if number <= 4 else 10_000)
            for number in range(1, 9)
        ]
        comparison = compare_four_week_blocks(weeks)
        distance = comparison["metrics"]["running_distance_m"]

        self.assertIsNone(distance["percentage_change"])
        self.assertEqual(
            "not_applicable_zero_baseline",
            distance["percentage_status"],
        )


class RaceContextTests(unittest.TestCase):
    def test_missing_context_defaults_to_sixteen_weeks(self):
        context, review_weeks = normalise_race_context(
            None,
            date(2026, 7, 29),
        )

        self.assertIsNone(context)
        self.assertEqual(16, review_weeks)

    def test_other_distances_default_to_sixteen_weeks(self):
        for race_context in (
            {"raceType": "custom", "distanceKm": 30},
            {"raceType": "10k"},
        ):
            with self.subTest(race_context=race_context):
                context, review_weeks = normalise_race_context(
                    race_context,
                    date(2026, 7, 29),
                )

                self.assertIsNotNone(context)
                self.assertEqual(16, review_weeks)

    def test_marathon_context_calculates_target_pace_and_window(self):
        context, review_weeks = normalise_race_context(
            {
                "event": {
                    "distance_type": "maraton",
                    "race_date": "2026-12-06",
                    "name": "Carrera objetivo",
                },
                "goal": {
                    "type": "target_time",
                    "target_time": "03:30:00",
                },
            },
            date(2026, 7, 29),
        )

        self.assertEqual(16, review_weeks)
        self.assertEqual("user_provided", context["source"])
        self.assertEqual("marathon", context["event"]["distance_type"])
        self.assertEqual(42_195.0, context["event"]["distance_m"])
        self.assertEqual(12_600, context["goal"]["target_time_s"])
        self.assertAlmostEqual(
            298.6,
            context["goal"]["target_pace_s_per_km"],
            places=1,
        )

    def test_context_rejects_implausible_distance(self):
        with self.assertRaisesRegex(ValueError, "distancia"):
            normalise_race_context(
                {"distance_type": "custom", "distance_m": 999},
                date(2026, 1, 1),
            )

    def test_launcher_context_keeps_training_goal_and_user_notes(self):
        context, review_weeks = normalise_race_context(
            {
                "raceType": "half_marathon",
                "distanceKm": 21.097,
                "raceDate": "2026-10-25",
                "goalType": "training",
                "terrain": "Asfalto con cuestas",
                "expectedClimate": "Templado",
                "recentPerformance": "10 km controlados en junio",
                "trainingConstraints": "Descansar los viernes",
            },
            date(2026, 7, 29),
        )

        self.assertEqual(12, review_weeks)
        self.assertEqual("training", context["goal"]["type"])
        self.assertEqual(
            "Asfalto con cuestas",
            context["availability"]["terrain"],
        )
        self.assertEqual(
            "10 km controlados en junio",
            context["experience"]["recent_performance_note"],
        )
        self.assertEqual(
            "Descansar los viernes",
            context["availability"]["restrictions"],
        )

    def test_explicit_zero_availability_is_not_treated_as_missing(self):
        context, _ = normalise_race_context(
            {
                "raceType": "marathon",
                "availableDaysPerWeek": 0,
                "strengthDaysPerWeek": 0,
                "availableMinutesPerWeek": 0,
            },
            date(2026, 7, 29),
        )

        availability = context["availability"]
        self.assertEqual(0, availability["available_days_per_week"])
        self.assertEqual(0, availability["strength_days_per_week"])
        self.assertEqual(0.0, availability["weekly_time_available_s"])

    def test_local_configuration_rejects_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "context.json"
            path.write_text(
                json.dumps({"email": "persona@example.invalid"}),
                encoding="utf-8",
            )

            with self.assertRaisesRegex(ValueError, "campo no permitido"):
                load_local_json(path, "contexto de prueba")


class JournalTests(unittest.TestCase):
    def test_zero_pain_is_preserved_and_free_text_is_opt_in(self):
        raw = {
            "entries": [{
                "date": "2026-01-07",
                "pain_0_10": 0,
                "fatigue_1_5": 3,
                "note": "Texto privado de prueba",
            }]
        }

        private = normalise_journal(raw)
        opted_in = normalise_journal(raw, include_free_text=True)

        self.assertEqual(0, private[0]["pain_0_10"])
        self.assertNotIn("note", private[0])
        self.assertEqual("Texto privado de prueba", opted_in[0]["note"])

    def test_private_note_is_never_exported(self):
        journal = normalise_journal(
            {
                "date": "2026-01-07",
                "note": "No exportar",
                "private": True,
            },
            include_free_text=True,
        )

        self.assertNotIn("note", journal[0])

    def test_launcher_comment_requires_its_own_explicit_consent(self):
        raw = {
            "entries": [{
                "calendarDate": "2026-01-07",
                "activityId": "activity_000000000001",
                "privateComment": "Calor y malas sensaciones",
                "includeCommentInExport": True,
                "fatigue1To5": 4,
                "motivation1To5": 2,
                "lifeStress1To10": 6,
            }]
        }

        exported = normalise_journal(raw)

        self.assertEqual("activity_000000000001", exported[0]["activity_ref"])
        self.assertEqual("Calor y malas sensaciones", exported[0]["note"])
        self.assertEqual(4, exported[0]["fatigue_1_5"])
        self.assertEqual(2, exported[0]["motivation_1_5"])
        self.assertEqual(6, exported[0]["life_stress_1_10"])

    def test_text_false_is_not_accepted_as_comment_consent(self):
        exported = normalise_journal({
            "date": "2026-01-07",
            "privateComment": "No debe salir",
            "includeCommentInExport": "false",
        })

        self.assertNotIn("note", exported[0])
        self.assertNotIn("comment_export_consent", exported[0])

    def test_out_of_range_scores_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "pain_0_10"):
            normalise_journal({
                "date": "2026-01-07",
                "pain_0_10": 11,
            })

    def test_negative_nutrition_values_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "no puede ser negativo"):
            normalise_journal({
                "date": "2026-01-07",
                "carbohydrates_g_per_hour": -1,
            })

    def test_raw_numeric_activity_id_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "referencia privada válida"):
            normalise_journal({
                "date": "2026-01-07",
                "activityId": "123456789",
                "pain": 2,
            })


class PrivateReferenceTests(unittest.TestCase):
    def test_pseudonym_is_stable_for_same_private_secret(self):
        first = private_reference("activity", 123456789, REFERENCE_SECRET)
        second = private_reference("activity", 123456789, REFERENCE_SECRET)

        self.assertEqual(first, second)
        self.assertNotIn("123456789", first)

    def test_pseudonym_changes_for_a_different_profile_secret(self):
        first = private_reference("activity", 123456789, REFERENCE_SECRET)
        second = private_reference("activity", 123456789, b"x" * 32)

        self.assertNotEqual(first, second)

    def test_private_secret_has_backup_and_is_stable(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            first = load_or_create_reference_secret(cache)
            second = load_or_create_reference_secret(cache)

            self.assertEqual(first, second)
            self.assertTrue((cache / ".privacy_reference_key").exists())
            self.assertTrue((cache / ".privacy_reference_key.bak").exists())

    def test_corrupt_private_secret_is_restored_from_backup(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            original = load_or_create_reference_secret(cache)
            key_path = cache / ".privacy_reference_key"
            key_path.write_text("contenido dañado", encoding="utf-8")

            recovered = load_or_create_reference_secret(cache)

            self.assertEqual(original, recovered)
            self.assertEqual(original.hex(), key_path.read_text().strip())

    def test_valid_main_secret_repairs_stale_or_corrupt_backup(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            original = load_or_create_reference_secret(cache)
            backup_path = cache / ".privacy_reference_key.bak"
            backup_path.write_text("dañada", encoding="utf-8")

            recovered = load_or_create_reference_secret(cache)

            self.assertEqual(original, recovered)
            self.assertEqual(
                original.hex(),
                backup_path.read_text(encoding="ascii").strip(),
            )

    def test_corrupt_private_secret_and_backup_are_never_rotated(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory)
            key_path = cache / ".privacy_reference_key"
            backup_path = cache / ".privacy_reference_key.bak"
            key_path.write_text("dañada", encoding="utf-8")
            backup_path.write_text("también dañada", encoding="utf-8")

            with self.assertRaisesRegex(RuntimeError, "dañada"):
                load_or_create_reference_secret(cache)

            self.assertEqual("dañada", key_path.read_text(encoding="utf-8"))
            self.assertEqual(
                "también dañada",
                backup_path.read_text(encoding="utf-8"),
            )

    def test_activity_catalog_contains_no_raw_id_name_or_exact_time(self):
        catalog = activity_catalog_entry(
            {
                "activityId": 123456789,
                "activityName": "Ruta privada",
                "startTimeLocal": "2026-01-07 06:43:21",
                "activityType": {"typeKey": "running"},
                "distance": 10_000,
                "duration": 3_600,
            },
            REFERENCE_SECRET,
        )
        serialized = json.dumps(catalog)

        self.assertNotIn("123456789", serialized)
        self.assertNotIn("Ruta privada", serialized)
        self.assertNotIn("06:43:21", serialized)
        self.assertEqual("2026-01-07", catalog["date"])


class ConservativeClassificationTests(unittest.TestCase):
    def test_unstructured_run_remains_unknown(self):
        activity = anonymous_activity(
            "activity_000000000001",
            "2026-01-06",
            distance_m=8_000,
            duration_s=2_700,
        )
        timeline = build_weekly_timeline(
            [activity],
            [],
            date(2026, 1, 5),
            date(2026, 1, 11),
        )

        classified = classify_activities([activity], timeline)[0]

        self.assertEqual("unknown", classified["classification"]["type"])
        self.assertEqual(
            "insufficient_evidence",
            classified["classification"]["source"],
        )

    def test_manual_intention_has_priority_and_is_auditable(self):
        activity = anonymous_activity(
            "activity_000000000002",
            "2026-01-08",
        )
        timeline = build_weekly_timeline(
            [activity],
            [],
            date(2026, 1, 5),
            date(2026, 1, 11),
        )
        journal = [{
            "activity_ref": "activity_000000000002",
            "intended_session_type": "threshold",
        }]

        classified = classify_activities([activity], timeline, journal)[0]

        self.assertEqual("threshold", classified["classification"]["type"])
        self.assertEqual(
            "user_provided",
            classified["classification"]["source"],
        )
        self.assertEqual(1.0, classified["classification"]["confidence"])


class GoalPaceExposureTests(unittest.TestCase):
    def test_only_laps_inside_target_band_are_counted(self):
        context = {
            "goal": {"target_pace_s_per_km": 300},
        }
        activities = [
            anonymous_activity(
                "activity_000000000001",
                "2026-01-06",
                laps=[
                    {
                        "average_pace_s_per_km": 300,
                        "distance_m": 1_000,
                        "duration_s": 300,
                    },
                    {
                        "average_pace_s_per_km": 315,
                        "distance_m": 2_000,
                        "duration_s": 630,
                    },
                    {
                        "average_pace_s_per_km": 316,
                        "distance_m": 4_000,
                        "duration_s": 1_264,
                    },
                ],
            )
        ]

        exposure = calculate_goal_pace_exposure(activities, context)

        self.assertEqual("available", exposure["status"])
        self.assertEqual(3_000, exposure["distance_in_band_m"])
        self.assertEqual(930, exposure["duration_in_band_s"])
        self.assertEqual(2, exposure["matching_laps"])
        self.assertEqual(1, exposure["matching_activities"])

    def test_target_pace_is_required(self):
        exposure = calculate_goal_pace_exposure([], None)

        self.assertEqual("unavailable", exposure["status"])


class PrivacyAuditTests(unittest.TestCase):
    def test_privacy_allows_sport_location_but_rejects_identity(self):
        allowed = privacy_audit(
            {
                "name": "Cuestas del parque",
                "latitude": 39.123456789,
                "longitude": -0.123456789,
                "encodedPolyline": "abc123",
                "locationName": "Parque ficticio",
                "gear": {"displayName": "Zapatillas rápidas"},
            },
        )
        self.assertTrue(allowed["passed"])
        rejected = privacy_audit({"ownerFullName": "Persona ficticia"})
        self.assertFalse(rejected["passed"])

    def test_privacy_never_allows_secrets(self):
        rejected = privacy_audit({"accessToken": "secreto-ficticio"})
        self.assertFalse(rejected["passed"])

    def test_safe_local_references_pass(self):
        audit = privacy_audit({
            "activities": [{
                "activity_ref": "activity_abcdef123456",
                "gear_ref": "gear_abcdef123456",
            }]
        })

        self.assertTrue(audit["passed"])

    def test_forbidden_keys_and_known_values_fail(self):
        audit = privacy_audit(
            {
                "activities": [{
                    "activityId": 123456789,
                    "latitude": 39.123,
                }]
            },
            forbidden_values=[123456789],
        )

        self.assertFalse(audit["passed"])
        self.assertIn(
            "activities[0].activityId",
            audit["forbidden_key_paths"],
        )
        self.assertNotIn(
            "activities[0].latitude",
            audit["forbidden_key_paths"],
        )
        self.assertTrue(audit["forbidden_values_detected"])

    def test_identity_device_address_identifier_and_url_fields_fail(self):
        audit = privacy_audit({
            "profile": {
                "device_id": "device-private",
                "full_name": "Persona privada",
                "birth_date": "1980-01-01",
                "profile_url": "https://private.invalid/profile",
            },
            "activity": {
                "start_city": "Ciudad privada",
                "location_name": "Lugar privado",
                "address": "Calle privada",
                "workout_id": "workout-private",
            },
        })

        self.assertFalse(audit["passed"])
        self.assertEqual(
            {
                "activity.address",
                "activity.workout_id",
                "profile.birth_date",
                "profile.device_id",
                "profile.full_name",
                "profile.profile_url",
            },
            set(audit["forbidden_key_paths"]),
        )

    def test_short_coordinates_are_allowed_but_links_are_removed(self):
        audit = privacy_audit({
            "private": {
                "lat": 39.1,
                "lon": -0.3,
                "lng": -0.3,
                "link": "https://private.invalid",
            },
            "safe": {
                "plateau": 3,
                "linked_metric": 4,
            },
        })

        self.assertFalse(audit["passed"])
        self.assertEqual(
            {
                "private.link",
            },
            set(audit["forbidden_key_paths"]),
        )

    def test_private_values_and_identifiers_use_exact_comparison(self):
        embedded = privacy_audit(
            {
                "profile": {"timezone": "Europe/Madrid"},
                "technical": {
                    "legacy_reference": "prefix-123456789-suffix",
                },
            },
            forbidden_values=["Madrid"],
            forbidden_identifiers=["123456789"],
        )

        self.assertTrue(embedded["passed"])

        exact_private_value = privacy_audit(
            {
                "safe_field": "Madrid",
                "legacy_reference": "123456789",
            },
            forbidden_values=["Madrid"],
            forbidden_identifiers=["123456789"],
        )
        self.assertFalse(exact_private_value["passed"])
        self.assertEqual(
            ["123…", "Mad…"],
            exact_private_value["forbidden_values_detected"],
        )
        self.assertEqual(
            ["legacy_reference", "safe_field"],
            exact_private_value["forbidden_value_paths"],
        )

    def test_exception_summary_never_copies_raw_message(self):
        raw_secret = "https://sso.example.invalid?token=123456789"

        summary = _safe_exception_reason(RuntimeError(raw_secret))

        self.assertEqual("RuntimeError", summary)
        self.assertNotIn(raw_secret, summary)


class PartialExportContractTests(unittest.TestCase):
    def setUp(self):
        self.previous_compact_mode = garmin_export._compact_mode
        garmin_export._compact_mode = True

    def tearDown(self):
        garmin_export._compact_mode = self.previous_compact_mode

    def test_partial_status_is_inside_shared_semantic_model(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            exporter = GarminExporter(
                api=Mock(),
                out_dir=root,
                days=7,
                max_activities=10,
                cache=ExportCache(
                    root,
                    enabled=False,
                    cache_dir=root / ".cache",
                ),
                explicit_start_date=date(2026, 1, 5),
                explicit_end_date=date(2026, 1, 11),
            )
            exporter.semantic_model = {"export_metadata": {}}
            exporter.errors = [
                "Activities: fallo técnico (RuntimeError)",
            ]

            exporter._finalize_semantic_model()

        metadata = exporter.semantic_model["export_metadata"]
        quality = exporter.semantic_model["data_quality"]
        self.assertEqual("partial", metadata["export_status"])
        self.assertEqual(["Activities"], metadata["failed_sections"])
        self.assertEqual(
            "Activities",
            quality["export_errors"][0]["section"],
        )
        self.assertTrue(any(
            issue["code"] == "EXPORT_SECTION_ERROR"
            for issue in quality["issues"]
        ))

    def test_real_identifier_is_audited_before_writing(self):
        raw_identifier = "123456789"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            exporter = GarminExporter(
                api=Mock(),
                out_dir=root,
                days=7,
                max_activities=10,
                cache=ExportCache(
                    root,
                    enabled=False,
                    cache_dir=root / ".cache",
                ),
                explicit_start_date=date(2026, 1, 5),
                explicit_end_date=date(2026, 1, 11),
            )
            exporter.semantic_model = {
                "export_metadata": {},
                "profile": {"legacy_reference": raw_identifier},
            }
            exporter.sensitive_identifiers.add(raw_identifier)

            with self.assertRaisesRegex(RuntimeError, "privacidad"):
                exporter._finalize_semantic_model()

    def test_observed_profile_identifier_is_audited_even_under_safe_looking_key(self):
        raw_device_identifier = "device-987654321"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            exporter = GarminExporter(
                api=Mock(),
                out_dir=root,
                days=7,
                max_activities=10,
                cache=ExportCache(
                    root,
                    enabled=False,
                    cache_dir=root / ".cache",
                ),
                explicit_start_date=date(2026, 1, 5),
                explicit_end_date=date(2026, 1, 11),
            )
            exporter._remember_sensitive_payload({
                "devices": [{"deviceId": raw_device_identifier}],
            })
            exporter.semantic_model = {
                "export_metadata": {},
                "profile": {"legacy_reference": raw_device_identifier},
            }

            with self.assertRaisesRegex(RuntimeError, "privacidad"):
                exporter._finalize_semantic_model()

    def test_realistic_activity_identity_is_filtered_before_final_audit(self):
        raw = {
            "summary": {
                "activityId": 123456789,
                "activityName": "Rodaje con cuestas",
                "activityType": {"typeKey": "running"},
                "startTimeLocal": "2026-01-10 08:00:00",
                "ownerFullName": "Persona ficticia",
                "ownerFirstName": "Persona",
                "publicDisplayName": "persona_ficticia",
                "startLatitude": 39.123456789,
                "startLongitude": -0.123456789,
                "distance": 10_000,
                "duration": 3_600,
                "elevationGain": 75,
            },
            "detail": {
                "deviceSerialNumber": "SERIAL-FICTICIO-123",
                "primaryUnitId": 987654321,
                "summaryDTO": {},
            },
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            exporter = GarminExporter(
                api=Mock(),
                out_dir=root,
                days=7,
                max_activities=10,
                cache=ExportCache(
                    root,
                    enabled=False,
                    cache_dir=root / ".cache",
                ),
                explicit_start_date=date(2026, 1, 5),
                explicit_end_date=date(2026, 1, 11),
            )
            exporter._remember_sensitive_payload(raw)
            exporter.sensitive_identifiers.add("123456789")
            activity = _compact_activity(
                raw,
                reference_secret=exporter.reference_secret,
            )
            exporter.compact_activities = [activity]
            exporter.semantic_model = {
                "export_metadata": {},
                "activities": [activity],
            }

            exporter._finalize_semantic_model()

        self.assertNotIn("source_activity_data", activity)
        self.assertNotIn("unmapped_sport_data", activity)
        self.assertEqual(
            39.123456789,
            activity["coordinates"]["start"]["latitude"],
        )
        self.assertTrue(
            exporter.semantic_model["data_quality"]["privacy_audit"]["passed"]
        )
        reduction = exporter.semantic_model["data_quality"][
            "compact_data_reduction"
        ]
        self.assertFalse(reduction["raw_activity_copy_exported"])
        self.assertTrue(reduction["normalised_sports_data_exported_once"])

    def test_safe_call_failure_marks_semantic_export_partial_without_raw_message(self):
        raw_message = "https://private.invalid?token=secret-value"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            exporter = GarminExporter(
                api=Mock(),
                out_dir=root,
                days=7,
                max_activities=10,
                cache=ExportCache(
                    root,
                    enabled=False,
                    cache_dir=root / ".cache",
                ),
                explicit_start_date=date(2026, 1, 5),
                explicit_end_date=date(2026, 1, 11),
            )
            exporter.semantic_model = {"export_metadata": {}}

            def failing_endpoint():
                raise RuntimeError(raw_message)

            _set_safe_call_failure_handler(
                lambda endpoint, reason:
                    exporter._record_endpoint_failure(
                        "Daily Health",
                        endpoint,
                        reason,
                    )
            )
            try:
                self.assertIsNone(safe_call(failing_endpoint))
            finally:
                _set_safe_call_failure_handler(None)

            exporter._finalize_semantic_model()

        metadata = exporter.semantic_model["export_metadata"]
        quality = exporter.semantic_model["data_quality"]
        serialized = json.dumps(exporter.semantic_model)
        self.assertEqual("partial", metadata["export_status"])
        self.assertEqual(["Daily Health"], metadata["failed_sections"])
        self.assertTrue(any(
            issue["code"] == "ENDPOINT_ERRORS"
            for issue in quality["issues"]
        ))
        self.assertNotIn(raw_message, serialized)


class TrainingMetricDateTests(unittest.TestCase):
    def test_list_records_are_split_by_their_own_effective_date(self):
        result, snapshots = _compact_training(
            {
                "race_predictions": [
                    {"calendarDate": "2025-12-15", "time5K": 1_500},
                    {"calendarDate": "2026-01-15", "time5K": 1_450},
                    {"calendarDate": "2026-02-15", "time5K": 1_400},
                ]
            },
            date(2026, 1, 1),
            date(2026, 1, 31),
        )

        self.assertEqual(
            "2025-12-15",
            result["latest_before_or_within_period"]["race_predictions"][0][
                "date"
            ],
        )
        self.assertEqual(
            "2026-01-15",
            result["historical_period_data"]["race_predictions"][0]["date"],
        )
        self.assertEqual(
            "2026-02-15",
            result["current_snapshot"]["race_predictions"][0]["date"],
        )
        self.assertEqual("race_predictions", snapshots[0]["metric"])
        self.assertEqual(["2026-02-15"], snapshots[0]["effective_dates"])

    def test_indivisible_metric_with_period_and_future_dates_is_snapshot(self):
        result, snapshots = _compact_training(
            {
                "training_status": {
                    "records": [
                        {"calendarDate": "2026-01-15", "status": "within"},
                        {"calendarDate": "2026-02-15", "status": "future"},
                    ]
                }
            },
            date(2026, 1, 1),
            date(2026, 1, 31),
        )

        self.assertNotIn(
            "training_status",
            result.get("historical_period_data", {}),
        )
        self.assertIn("training_status", result["current_snapshot"])
        self.assertEqual(["2026-02-15"], snapshots[0]["effective_dates"])


class QualityPreservationTests(unittest.TestCase):
    def test_quality_report_records_automatic_privacy(self):
        quality = build_quality_report(
            [],
            [],
            date(2026, 1, 1),
            date(2026, 1, 1),
        )
        self.assertEqual(
            "redact_personal_identifiers",
            quality["privacy"]["mode"],
        )
        self.assertTrue(
            quality["privacy"]["coordinates_and_locations_exported"]
        )
        self.assertTrue(
            quality["privacy"]["activity_titles_exported_by_default"]
        )

    def test_legacy_warnings_and_missing_critical_data_are_preserved(self):
        quality = build_quality_report(
            [],
            [],
            date(2026, 1, 1),
            date(2026, 1, 1),
            legacy_quality={
                "warnings": ["Aviso controlado"],
                "missing_critical_data": ["Falta controlada"],
            },
        )

        self.assertEqual(["Aviso controlado"], quality["warnings"])
        self.assertEqual(
            ["Falta controlada"],
            quality["missing_critical_data"],
        )


class SelectedActivityTests(unittest.TestCase):
    def setUp(self):
        self.previous_compact_mode = garmin_export._compact_mode
        garmin_export._compact_mode = True

    def tearDown(self):
        garmin_export._compact_mode = self.previous_compact_mode

    def test_selected_private_reference_is_filtered_before_detail_calls(self):
        raw_activities = [
            {
                "activityId": 111111111,
                "activityName": "Actividad privada A",
                "activityType": {"typeKey": "running"},
                "startTimeLocal": "2026-01-06 07:00:00",
                "distance": 5_000,
                "duration": 1_800,
            },
            {
                "activityId": 222222222,
                "activityName": "Actividad privada B",
                "activityType": {"typeKey": "running"},
                "startTimeLocal": "2026-01-08 07:00:00",
                "distance": 10_000,
                "duration": 3_600,
            },
        ]
        labels = []

        def fake_safe_call(*args, **kwargs):
            label = kwargs.get("label")
            labels.append(label)
            if label == "activities_all":
                return raw_activities
            return None

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = ExportCache(
                root,
                enabled=False,
                cache_dir=root / ".cache",
            )
            exporter = GarminExporter(
                api=Mock(),
                out_dir=root,
                days=7,
                max_activities=10,
                cache=cache,
                explicit_start_date=date(2026, 1, 5),
                explicit_end_date=date(2026, 1, 11),
            )
            selected_ref = private_reference(
                "activity",
                222222222,
                exporter.reference_secret,
            )
            exporter.selected_activity_ref = selected_ref

            with patch("garmin_export.safe_call", side_effect=fake_safe_call):
                exporter.export_activities()

        self.assertEqual(1, len(exporter.compact_activities))
        self.assertEqual(
            selected_ref,
            exporter.compact_activities[0]["activity_ref"],
        )
        self.assertIn("act_222222222", labels)
        self.assertNotIn("act_111111111", labels)

    def test_raw_garmin_identifier_is_not_accepted_as_activity_reference(self):
        raw_activities = [{
            "activityId": 222222222,
            "activityType": {"typeKey": "running"},
            "startTimeLocal": "2026-01-07 08:00:00",
        }]
        labels = []

        def fake_safe_call(*args, **kwargs):
            labels.append(kwargs.get("label"))
            if kwargs.get("label") == "activities_all":
                return raw_activities
            return None

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            exporter = GarminExporter(
                api=Mock(),
                out_dir=root,
                days=7,
                max_activities=10,
                cache=ExportCache(
                    root,
                    enabled=False,
                    cache_dir=root / ".cache",
                ),
                explicit_start_date=date(2026, 1, 5),
                explicit_end_date=date(2026, 1, 11),
                selected_activity_ref="222222222",
            )

            with patch("garmin_export.safe_call", side_effect=fake_safe_call):
                with self.assertRaisesRegex(
                    ValueError,
                    "No se encontró la actividad",
                ):
                    exporter.export_activities()

        self.assertEqual(["activities_all"], labels)


if __name__ == "__main__":
    unittest.main()
