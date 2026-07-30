import json
import tempfile
import unittest
from datetime import date
from pathlib import Path
from unittest.mock import Mock

import garmin_export
from garmin_export import (
    ExportCache,
    GarminExporter,
    RateLimiter,
    _compact_activity,
    _compact_daily_record,
    _compact_hydration,
    _compact_lifestyle_entries,
    _compact_nutrition,
    _compact_personal_records,
    _compact_profile,
    _compact_training,
    _compact_gear_items,
    _enrich_activity_gear_from_catalog,
    _find_blood_pressure_measurements,
    _relative_manifest_paths,
    _sanitize_compact,
    _weekly_summary,
    _json,
)
from training_analysis import privacy_audit


class SemanticHelperTests(unittest.TestCase):
    def test_generic_sanitizer_removes_short_location_and_link_aliases(self):
        result = _sanitize_compact({
            "lat": 39.1,
            "lon": -0.3,
            "lng": -0.3,
            "link": "https://private.invalid",
            "nested": {"lat": 40.0, "safe_name": "dato técnico"},
            "plateau": 3,
            "linked_metric": 4,
        })

        self.assertEqual({
            "nested": {"safe_name": "dato técnico"},
            "plateau": 3,
            "linked_metric": 4,
        }, result)

    def test_active_goal_text_requires_explicit_free_text_consent(self):
        raw = {
            "active_goals": [{
                "goalId": 987654321,
                "goalName": "Objetivo de Persona Privada",
                "description": "Comentario privado",
                "targetValue": 100,
                "goalType": {
                    "name": "Distancia personalizada",
                    "typeKey": "distance",
                },
            }],
        }

        strict = _compact_personal_records(raw)
        opted_in = _compact_personal_records(raw, include_free_text=True)

        strict_text = json.dumps(strict, ensure_ascii=False)
        opted_in_text = json.dumps(opted_in, ensure_ascii=False)
        self.assertNotIn("Objetivo de Persona Privada", strict_text)
        self.assertNotIn("Comentario privado", strict_text)
        self.assertNotIn("Distancia personalizada", strict_text)
        self.assertEqual(
            100,
            strict["active_goals"][0]["targetValue"],
        )
        self.assertIn("Objetivo de Persona Privada", opted_in_text)
        self.assertIn("Comentario privado", opted_in_text)

    def test_manifest_paths_are_relative_and_cannot_escape_output(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "salida"
            nested = root / "partes" / "informe.txt"
            outside = Path(directory) / "fuera.txt"

            self.assertEqual(
                ["partes/informe.txt"],
                _relative_manifest_paths([nested], root),
            )
            with self.assertRaisesRegex(
                RuntimeError,
                "carpeta de salida",
            ):
                _relative_manifest_paths([outside], root)

    def test_profile_excludes_identity_and_exact_birth_date(self):
        raw = {
            "full_name": "Persona de prueba",
            "user_profile": {
                "id": 999,
                "userData": {
                    "birthDate": "1980-05-20",
                    "gender": "MALE",
                    "height": 180,
                },
            },
            "profile_settings": {
                "measurementSystem": "metric",
                "timeZone": "Europe/Madrid",
            },
            "devices": [
                {
                    "deviceId": 44,
                    "productDisplayName": "Reloj de prueba",
                    "serialNumber": "SECRETO",
                }
            ],
            "primary_device": {
                "PrimaryTrainingDevice": {"deviceId": 44}
            },
        }

        result = _compact_profile(raw, date(2026, 7, 28))
        text = json.dumps(result)

        self.assertEqual("male", result["sex"])
        self.assertEqual("Reloj de prueba", result["primary_watch"])
        self.assertNotIn("Persona de prueba", text)
        self.assertNotIn("1980-05-20", text)
        self.assertNotIn("SECRETO", text)

    def test_profile_never_uses_custom_device_display_name(self):
        result = _compact_profile(
            {
                "user_profile": {"userData": {}},
                "devices": [{
                    "deviceId": 123456,
                    "displayName": "Reloj de Persona Privada",
                    "shortName": "Reloj privado",
                }],
            },
            date(2026, 7, 29),
        )

        self.assertNotIn("primary_watch", result)

    def test_daily_record_normalises_sleep_and_hrv(self):
        raw = {
            "summary": {
                "totalSteps": 9000,
                "restingHeartRate": 52,
                "userProfileId": 999,
            },
            "sleep": {
                "dailySleepDTO": {
                    "sleepTimeSeconds": 25200,
                    "deepSleepSeconds": 3600,
                    "sleepStartTimestampLocal": "2026-07-01T23:00:00",
                    "sleepEndTimestampLocal": "2026-07-02T06:30:00",
                    "sleepScores": {"overall": {"value": 82}},
                    "userProfilePK": 999,
                }
            },
            "hrv": {
                "hrvSummary": {
                    "calendarDate": "2026-07-02",
                    "lastNightAvg": 48,
                    "lastNight5MinHigh": 70,
                    "status": "BALANCED",
                },
                "hrvReadings": [{"hrvValue": 47}],
            },
        }

        result = _compact_daily_record("2026-07-02", raw)
        text = json.dumps(result)

        self.assertEqual(25200, result["sleep"]["total_sleep_s"])
        self.assertEqual(48, result["hrv"]["overnight_average_ms"])
        self.assertNotIn("hrvReadings", text)
        self.assertNotIn("userProfile", text)

    def test_private_activity_keeps_title_laps_zones_feedback_and_gear(self):
        raw = {
            "summary": {
                "activityId": 42,
                "activityName": "Rodaje",
                "activityType": {"typeKey": "running"},
                "startTimeLocal": "2026-07-20 08:00:00",
                "distance": 10000,
                "duration": 3600,
                "averageSpeed": 2.777777,
                "averageHR": 145,
                "ownerFullName": "No exportar",
                "startLatitude": 39.0,
            },
            "detail": {
                "summaryDTO": {
                    "directWorkoutFeel": 75,
                    "directWorkoutRpe": 50,
                }
            },
            "splits": {
                "lapDTOs": [
                    {
                        "lapIndex": 1,
                        "distance": 1000,
                        "duration": 350,
                        "averageSpeed": 2.857,
                    }
                ]
            },
            "hr_zones": [
                {"zoneNumber": 2, "secsInZone": 1800, "zoneLowBoundary": 130}
            ],
            "gear": [
                {
                    "gearPk": 987654321,
                    "uuid": "gear-1",
                    "displayName": "Zapatillas de competición",
                    "gearMakeName": "ASICS",
                    "gearModelName": "METASPEED SKY PARIS",
                    "gearTypeName": "running_shoes",
                }
            ],
        }

        result = _compact_activity(raw)
        text = json.dumps(result)

        self.assertEqual(1, len(result["laps"]))
        self.assertEqual(
            5,
            result["self_evaluation"]["perceived_exertion_1_10"],
        )
        gear = result["gear"][0]
        self.assertRegex(gear["gear_ref"], r"^gear_[0-9a-f]{12}$")
        self.assertEqual("Zapatillas de competición", gear["gear_name"])
        self.assertEqual("ASICS", gear["manufacturer"])
        self.assertEqual("METASPEED SKY PARIS", gear["model"])
        self.assertTrue(gear["gear_name_user_provided"])
        self.assertFalse(gear["model_user_provided"])
        self.assertNotIn("custom_name", gear)
        self.assertNotIn("gear-1", text)
        self.assertNotIn("987654321", text)
        self.assertEqual("Rodaje", result["name"])
        self.assertNotIn('"activity_id"', text)
        self.assertIn("latitude", text.lower())
        self.assertNotIn("No exportar", text)
        audit = privacy_audit(
            {"activities": [result]},
            forbidden_identifiers=[987654321],
        )
        self.assertTrue(audit["passed"], audit)

    def test_private_export_keeps_complete_sport_data(self):
        raw = self._complete_sport_activity()
        result = _compact_activity(
            raw,
            include_series=True,
            include_free_text=True,
        )
        self.assertEqual("Cuestas del parque", result["name"])
        self.assertEqual("Trabajo progresivo", result["description"])
        self.assertEqual(39.123456789, result["coordinates"]["start"]["latitude"])
        self.assertEqual(-0.123456789, result["coordinates"]["start"]["longitude"])
        previous_mode = garmin_export._compact_mode
        garmin_export._compact_mode = True
        try:
            rendered = _json(result)
        finally:
            garmin_export._compact_mode = previous_mode
        self.assertIn("39.123456789", rendered)
        self.assertIn("-0.123456789", rendered)
        self.assertEqual(
            "abc123",
            result["route_geometry"]["detail.encodedPolyline"],
        )
        self.assertNotIn(
            "activityId",
            result["route_geometry"]["detail.map"],
        )
        self.assertEqual(
            "map-track-123",
            result["route_geometry"]["detail.map"]["encodedPolyline"],
        )
        self.assertEqual(120.5, result["start_elevation_m"])
        self.assertEqual(128.0, result["end_elevation_m"])
        self.assertEqual(7.5, result["elevation_net_change_m"])
        self.assertEqual(330.0, result["grade_adjusted_pace_s_per_km"])
        lap = result["laps"][0]
        self.assertEqual(18.0, lap["elevation_gain_m"])
        self.assertEqual(10.5, lap["elevation_loss_m"])
        self.assertEqual(7.5, lap["elevation_net_change_m"])
        self.assertEqual(120.5, lap["start_elevation_m"])
        self.assertEqual(128.0, lap["end_elevation_m"])
        self.assertEqual(118.0, lap["minimum_elevation_m"])
        self.assertEqual(132.0, lap["maximum_elevation_m"])
        self.assertEqual(325.0, lap["grade_adjusted_pace_s_per_km"])
        descriptors = result["activity_series"]["metric_descriptors"]
        fields = {item["field"] for item in descriptors}
        self.assertTrue({
            "latitude_deg",
            "longitude_deg",
            "elevation_raw",
            "grade_adjusted_speed_raw",
        }.issubset(fields))
        source = result["source_activity_data"]
        self.assertEqual("abc123", source["detail"]["encodedPolyline"])
        self.assertIn("startLatitude", source["summary"])
        self.assertNotIn("activityId", source["summary"])
        self.assertNotIn("ownerFullName", source["summary"])
        self.assertNotIn("ownerFirstName", source["summary"])
        self.assertNotIn("publicDisplayName", source["summary"])
        self.assertNotIn("deviceSerialNumber", source["detail"])
        self.assertNotIn("primaryUnitId", source["detail"])
        self.assertNotIn("gearUUID", source["detail"])
        self.assertNotIn("metadataDTO", source["detail"])
        self.assertNotIn("Persona privada ficticia", rendered)
        audit = privacy_audit({"activities": [result]})
        self.assertTrue(audit["passed"], audit)

    def test_activity_title_is_always_kept_but_notes_require_consent(self):
        without_text = _compact_activity(self._complete_sport_activity())
        self.assertEqual("Cuestas del parque", without_text["name"])
        self.assertNotIn("description", without_text)
        self.assertNotIn(
            "description",
            without_text["source_activity_data"]["summary"],
        )
        with_text = _compact_activity(
            self._complete_sport_activity(),
            include_free_text=True,
        )
        self.assertEqual("Trabajo progresivo", with_text["description"])

    @staticmethod
    def _complete_sport_activity():
        return {
            "summary": {
                "activityId": 42,
                "activityName": "Cuestas del parque",
                "description": "Trabajo progresivo",
                "ownerFullName": "Persona ficticia",
                "ownerFirstName": "Persona",
                "publicDisplayName": "persona_ficticia",
                "activityType": {"typeKey": "running"},
                "startTimeLocal": "2026-07-20 08:00:00",
                "distance": 10000,
                "duration": 3600,
                "averageGradeAdjustedSpeed": 1000 / 330,
                "startLatitude": 39.123456789,
                "startLongitude": -0.123456789,
                "endLatitude": 39.223456789,
                "endLongitude": -0.223456789,
                "startElevation": 120.5,
                "endElevation": 128.0,
                "minElevation": 118.0,
                "maxElevation": 150.0,
                "elevationGain": 75.0,
                "elevationLoss": 60.0,
            },
            "detail": {
                "encodedPolyline": "abc123",
                "metadataDTO": {
                    "userInfoDto": {
                        "displayname": "Persona privada ficticia",
                    },
                },
                "map": {
                    "activityId": 42,
                    "encodedPolyline": "map-track-123",
                },
                "gearUUID": "gear-fictitious-uuid",
                "deviceSerialNumber": "SERIAL-FICTICIO",
                "primaryUnitId": 987654,
                "summaryDTO": {},
            },
            "splits": {
                "lapDTOs": [{
                    "lapIndex": 1,
                    "distance": 1000,
                    "duration": 340,
                    "averageGradeAdjustedSpeed": 1000 / 325,
                    "startElevation": 120.5,
                    "endElevation": 128.0,
                    "minElevation": 118.0,
                    "maxElevation": 132.0,
                    "elevationGain": 18.0,
                    "elevationLoss": 10.5,
                }],
            },
            "details": {
                "metricDescriptors": [
                    {"key": "directLatitude", "metricsIndex": 0, "unit": {"key": "degree"}},
                    {"key": "directLongitude", "metricsIndex": 1, "unit": {"key": "degree"}},
                    {"key": "directElevation", "metricsIndex": 2, "unit": {"key": "meter"}},
                    {"key": "directGradeAdjustedSpeed", "metricsIndex": 3, "unit": {"key": "mps"}},
                ],
                "activityDetailMetrics": [
                    {"metrics": [39.123456789, -0.123456789, 120.5, 3.1]},
                    {"metrics": [39.124456789, -0.124456789, 121.0, 3.2]},
                ],
            },
        }

    def test_custom_model_replaces_generic_catalog_model(self):
        result = _compact_gear_items([{
            "gearPk": 123456789,
            "uuid": "private-gear-uuid",
            "userProfilePk": 987654321,
            "gearMakeName": "Other",
            "gearModelName": "Other",
            "displayName": "Zapatillas rápidas",
            "customMakeModel": "Nike Vaporfly 3",
            "gearTypeName": "Shoes",
        }])

        self.assertEqual(1, len(result))
        gear = result[0]
        self.assertEqual("Zapatillas rápidas", gear["gear_name"])
        self.assertNotIn("manufacturer", gear)
        self.assertEqual("Nike Vaporfly 3", gear["model"])
        self.assertTrue(gear["gear_name_user_provided"])
        self.assertTrue(gear["model_user_provided"])
        encoded = json.dumps(gear, ensure_ascii=False)
        self.assertNotIn("123456789", encoded)
        self.assertNotIn("987654321", encoded)
        self.assertNotIn("private-gear-uuid", encoded)

    def test_activity_gear_is_enriched_from_global_catalog(self):
        activities = [{
            "activity_ref": "activity_abcdef123456",
            "gear": [{
                "gear_ref": "gear_abcdef123456",
                "type": "Shoes",
            }],
        }]
        catalog = [{
            "gear_ref": "gear_abcdef123456",
            "gear_name": "Zapatillas de competición",
            "manufacturer": "ASICS",
            "model": "METASPEED SKY PARIS",
            "gear_name_user_provided": True,
            "model_user_provided": False,
            "type": "Running Shoes",
        }]

        _enrich_activity_gear_from_catalog(activities, catalog)

        association = activities[0]["gear"][0]
        self.assertEqual(
            "Zapatillas de competición",
            association["gear_name"],
        )
        self.assertEqual("ASICS", association["manufacturer"])
        self.assertEqual("METASPEED SKY PARIS", association["model"])
        self.assertEqual(
            "Shoes",
            association["type"],
            "El catálogo no debe sustituir un valor ya asociado.",
        )

    def test_training_separates_future_snapshot(self):
        raw = {
            "morning_readiness": {
                "calendarDate": "2026-07-12",
                "score": 70,
            },
            "race_predictions": {
                "calendarDate": "2026-07-28",
                "time5K": 1500,
            },
            "cycling_ftp": {
                "calendarDate": "2026-06-20",
                "functionalThresholdPower": 250,
            },
        }

        result, snapshots = _compact_training(
            raw,
            date(2026, 7, 6),
            date(2026, 7, 12),
        )

        self.assertIn(
            "morning_readiness",
            result["historical_period_data"],
        )
        self.assertIn("race_predictions", result["current_snapshot"])
        self.assertIn(
            "cycling_ftp",
            result["latest_before_or_within_period"],
        )
        self.assertEqual("race_predictions", snapshots[0]["metric"])

    def test_unknown_training_metric_drops_unapproved_free_text(self):
        result, _ = _compact_training(
            {
                "training_status": {
                    "calendarDate": "2026-01-10",
                    "name": "Ruta privada junto a casa",
                    "description": "Texto privado",
                    "score": 72,
                },
            },
            date(2026, 1, 1),
            date(2026, 1, 31),
        )

        encoded = json.dumps(result, ensure_ascii=False)
        self.assertNotIn("Ruta privada", encoded)
        self.assertNotIn("Texto privado", encoded)
        self.assertIn('"score": 72', encoded)

    def test_empty_blood_pressure_is_not_a_measurement(self):
        self.assertEqual(
            [],
            _find_blood_pressure_measurements({
                "from": "2026-07-01",
                "until": "2026-07-07",
                "measurementSummaries": [],
            }),
        )

    def test_hydration_goal_only_is_omitted(self):
        self.assertIsNone(_compact_hydration({"goalInML": 2000}))
        self.assertEqual(
            750,
            _compact_hydration({"goalInML": 2000, "valueInML": 750})[
                "intake_ml"
            ],
        )

    def test_nutrition_settings_without_food_are_omitted(self):
        self.assertIsNone(_compact_nutrition({
            "food_log": {"loggedFoodsWithServingSizes": []},
            "settings": {"calorieGoal": 2000},
        }))

    def test_lifestyle_keeps_real_logs_but_excludes_intimate_behaviours(self):
        result = _compact_lifestyle_entries({
            "dailyLogsReport": [
                {"name": "Illness", "logStatus": "YES"},
                {"name": "Sexo en pareja", "logStatus": "YES"},
                {"name": "Rapports sexuels", "logStatus": "YES"},
                {"name": "Geschlechtsverkehr", "logStatus": "YES"},
                {"name": "Relação sexual", "logStatus": "YES"},
                {"name": "Unlogged catalogue entry"},
            ]
        })

        self.assertEqual(["illness"], [item["behaviour"] for item in result])

    def test_weekly_summary_uses_activities_once(self):
        result = _weekly_summary(
            [
                {
                    "sport": "running",
                    "distance_m": 10000,
                    "duration_s": 3600,
                    "training_load": 90,
                    "hr_zones": [{"zone": 2, "duration_s": 1800}],
                }
            ],
            [
                {
                    "steps": 12000,
                    "resting_heart_rate_bpm": 52,
                    "sleep": {"total_sleep_s": 25200},
                    "hrv": {"overnight_average_ms": 48},
                }
            ],
        )

        summary = result["weekly_summary"]
        self.assertEqual(1, summary["running_sessions"])
        self.assertEqual(10000, summary["running_distance_m"])
        self.assertEqual(3600, summary["total_training_duration_s"])


class SemanticExporterIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.previous_compact_mode = garmin_export._compact_mode
        self.previous_limiter = garmin_export._limiter
        garmin_export._compact_mode = True
        garmin_export._limiter = RateLimiter(base_delay=0)

    def tearDown(self):
        garmin_export._compact_mode = self.previous_compact_mode
        garmin_export._limiter = self.previous_limiter

    def test_recent_cached_activity_refreshes_feedback_and_fetches_gear(self):
        today = date.today()
        activity = {
            "activityId": 42,
            "activityName": "Rodaje",
            "activityType": {"typeKey": "running"},
            "startTimeLocal": f"{today.isoformat()} 08:00:00",
        }
        api = Mock()
        api.get_activities_by_date.return_value = [activity]
        api.get_activity.return_value = {
            "summaryDTO": {
                "startTimeLocal": f"{today.isoformat()} 08:00:00",
                "directWorkoutRpe": 60,
            }
        }
        api.get_activity_gear.return_value = [
            {
                "uuid": "gear-1",
                "displayName": "Zapatillas rápidas",
                "gearMakeName": "Nike",
                "gearModelName": "Vaporfly 3",
            }
        ]

        with tempfile.TemporaryDirectory() as temp_dir:
            cache = ExportCache(Path(temp_dir), enabled=True)
            cache.put_activity(42, {"summary": activity, "detail": {}})
            exporter = GarminExporter(
                api=api,
                out_dir=Path(temp_dir),
                days=1,
                max_activities=10,
                explicit_start_date=today,
                explicit_end_date=today,
                cache=cache,
            )
            exporter.export_activities()

        api.get_activity.assert_called_once_with(42)
        api.get_activity_gear.assert_called_once_with(42)
        self.assertEqual(
            6,
            exporter.compact_activities[0]["self_evaluation"][
                "perceived_exertion_1_10"
            ],
        )
        gear = exporter.compact_activities[0]["gear"][0]
        self.assertRegex(
            gear["gear_ref"],
            r"^gear_[0-9a-f]{12}$",
        )
        self.assertEqual("Zapatillas rápidas", gear["gear_name"])
        self.assertEqual("Nike", gear["manufacturer"])
        self.assertEqual("Vaporfly 3", gear["model"])
        self.assertNotIn("custom_name", gear)

    def test_global_gear_uses_profile_id_returned_by_current_library(self):
        api = Mock()
        api.get_user_profile.return_value = {"id": 123}
        api.get_gear.return_value = [
            {
                "uuid": "gear-1",
                "displayName": "Bicicleta de carretera",
                "gearMakeName": "Canyon",
                "gearModelName": "Ultimate CF SL",
            }
        ]
        api.get_gear_defaults.return_value = []
        api.get_gear_stats.return_value = {"totalDistance": 50000}
        exporter = GarminExporter(
            api=api,
            out_dir=Path("export"),
            days=1,
            max_activities=10,
            explicit_start_date=date(2026, 1, 1),
            explicit_end_date=date(2026, 1, 1),
        )

        exporter.export_gear()

        api.get_gear.assert_called_once_with("123")
        text = "\n".join(exporter.md)
        self.assertIn('"gear_ref": "gear_', text)
        self.assertIn('"gear_name": "Bicicleta de carretera"', text)
        self.assertIn('"manufacturer": "Canyon"', text)
        self.assertIn('"model": "Ultimate CF SL"', text)
        self.assertNotIn('"uuid"', text)

    def test_full_profile_still_contains_raw_fields(self):
        garmin_export._compact_mode = False
        with tempfile.TemporaryDirectory() as temp_dir:
            cache = ExportCache(Path(temp_dir), enabled=True)
            cache.put_section("profile", {
                "full_name": "Nombre completo de prueba",
                "devices": [{"serialNumber": "SERIE"}],
            })
            exporter = GarminExporter(
                api=Mock(),
                out_dir=Path(temp_dir),
                days=1,
                max_activities=10,
                cache=cache,
            )
            exporter.export_profile()
            output = "\n".join(exporter.md)

        self.assertIn("Nombre completo de prueba", output)
        self.assertIn("SERIE", output)

    def test_full_activity_still_contains_raw_detail_and_location(self):
        garmin_export._compact_mode = False
        activity = {
            "activityId": 42,
            "activityName": "Actividad completa",
            "activityType": {"typeKey": "running"},
            "startTimeLocal": "2026-01-01 08:00:00",
            "ownerFullName": "Nombre raw",
            "startLatitude": 39.0,
        }
        api = Mock()
        api.get_activities_by_date.return_value = [activity]
        with tempfile.TemporaryDirectory() as temp_dir:
            cache = ExportCache(Path(temp_dir), enabled=True)
            cache.put_activity(42, {
                "summary": activity,
                "detail": {"summaryDTO": {"distance": 5000}},
                "details": {"activityDetailMetrics": [{"metrics": [1]}]},
            })
            exporter = GarminExporter(
                api=api,
                out_dir=Path(temp_dir),
                days=1,
                max_activities=10,
                explicit_start_date=date(2026, 1, 1),
                explicit_end_date=date(2026, 1, 1),
                cache=cache,
            )
            exporter.export_activities()
            output = "\n".join(exporter.md)

        self.assertIn("Nombre raw", output)
        self.assertIn("startLatitude", output)
        self.assertIn("Detalle de las series temporales", output)


if __name__ == "__main__":
    unittest.main()
