import json
import os
import re
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
    _set_safe_call_failure_handler,
)
from training_analysis import activity_catalog_entry


REFERENCE_SECRET = bytes(range(32))
ACTIVITY_REFERENCE_PATTERN = re.compile(r"^activity_[0-9a-f]{12}$")


def daily_api(sleep_result=None, sleep_error=None):
    """Crea un doble que responde solo con los datos declarados."""
    api = Mock()
    endpoint_names = (
        "get_user_summary",
        "get_heart_rates",
        "get_rhr_day",
        "get_sleep_data",
        "get_all_day_stress",
        "get_spo2_data",
        "get_respiration_data",
        "get_hrv_data",
        "get_body_battery",
        "get_body_battery_events",
        "get_intensity_minutes_data",
        "get_all_day_events",
        "get_lifestyle_logging_data",
    )
    for endpoint_name in endpoint_names:
        getattr(api, endpoint_name).return_value = None
    api.get_sleep_data.return_value = sleep_result
    api.get_sleep_data.side_effect = sleep_error
    return api


class CacheRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.previous_compact_mode = garmin_export._compact_mode
        self.previous_limiter = garmin_export._limiter
        garmin_export._compact_mode = True
        garmin_export._limiter = RateLimiter(base_delay=0)
        _set_safe_call_failure_handler(None)

    def tearDown(self):
        _set_safe_call_failure_handler(None)
        garmin_export._compact_mode = self.previous_compact_mode
        garmin_export._limiter = self.previous_limiter

    @staticmethod
    def exporter(root, cache, api, export_date):
        return GarminExporter(
            api=api,
            out_dir=root,
            days=1,
            max_activities=10,
            cache=cache,
            explicit_start_date=export_date,
            explicit_end_date=export_date,
        )

    @staticmethod
    def export_daily_with_failure_capture(exporter):
        _set_safe_call_failure_handler(
            lambda endpoint, reason:
                exporter._record_endpoint_failure(
                    "Daily Health",
                    endpoint,
                    reason,
                )
        )
        try:
            exporter.export_daily_health()
        finally:
            _set_safe_call_failure_handler(None)
        exporter._finalize_semantic_model()

    def test_failed_daily_endpoint_is_retried_and_recovers_from_cache(self):
        export_date = date(2026, 1, 11)
        day_string = export_date.isoformat()
        private_error = (
            "https://private.invalid/sleep?token=never-export-this"
        )
        recovered_sleep = {
            "dailySleepDTO": {
                "calendarDate": day_string,
                "sleepTimeSeconds": 25_200,
                "deepSleepSeconds": 3_600,
            }
        }

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = ExportCache(
                root,
                enabled=True,
                cache_dir=root / ".cache",
            )

            first = self.exporter(
                root,
                cache,
                daily_api(sleep_error=RuntimeError(private_error)),
                export_date,
            )
            self.export_daily_with_failure_capture(first)

            cached_after_failure, complete_keys = cache.get_day_entry(
                day_string
            )
            self.assertIsNotNone(cached_after_failure)
            self.assertNotIn("sleep", cached_after_failure)
            self.assertNotIn("sleep", complete_keys)
            self.assertEqual(
                "partial",
                first.semantic_model["export_metadata"]["export_status"],
            )
            self.assertNotIn(
                private_error,
                json.dumps(first.semantic_model),
            )

            second_api = daily_api(sleep_result=recovered_sleep)
            second = self.exporter(
                root,
                cache,
                second_api,
                export_date,
            )
            self.export_daily_with_failure_capture(second)

            cached_after_recovery, complete_keys = cache.get_day_entry(
                day_string
            )

        self.assertEqual(
            "completed",
            second.semantic_model["export_metadata"]["export_status"],
        )
        self.assertEqual(
            25_200,
            second.compact_daily_records[0]["sleep"]["total_sleep_s"],
        )
        self.assertTrue(
            second.compact_daily_records[0]["sleep"]["valid_sleep"]
        )
        self.assertIn("sleep", complete_keys)
        self.assertEqual(recovered_sleep, cached_after_recovery["sleep"])
        second_api.get_sleep_data.assert_called_once_with(day_string)

    def test_recent_hydration_and_nutrition_prefer_fresh_responses(self):
        export_date = date(2026, 1, 11)
        day_string = export_date.isoformat()
        old_food = {
            "loggedFoodsWithServingSizes": [
                {"foodName": "Alimento antiguo", "calories": 100}
            ],
            "totalCalories": 100,
        }
        new_food = {
            "loggedFoodsWithServingSizes": [
                {"foodName": "Alimento nuevo", "calories": 250}
            ],
            "totalCalories": 250,
        }

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = ExportCache(
                root,
                enabled=True,
                cache_dir=root / ".cache",
            )
            cache.put_day(
                f"hydration_{day_string}",
                {"hydration": {"valueInML": 400}},
                complete_keys={"hydration"},
            )
            cache.put_day(
                f"nutrition_{day_string}",
                {
                    "food_log": old_food,
                    "meals": {"dailyMeals": []},
                    "settings": None,
                },
                complete_keys={"food_log", "meals"},
            )

            api = Mock()
            api.get_hydration_data.return_value = {"valueInML": 1_300}
            api.get_nutrition_daily_food_log.return_value = new_food
            api.get_nutrition_daily_meals.return_value = {
                "dailyMeals": [{"mealName": "Cena"}]
            }
            exporter = self.exporter(root, cache, api, export_date)

            exporter.export_hydration()
            exporter.export_nutrition()

            cached_hydration = cache.get_day(
                f"hydration_{day_string}"
            )
            cached_nutrition = cache.get_day(
                f"nutrition_{day_string}"
            )

        self.assertEqual(
            1_300,
            exporter.semantic_model["hydration"][day_string]["intake_ml"],
        )
        self.assertEqual(
            "Alimento nuevo",
            exporter.semantic_model["nutrition"][day_string][
                "logged_foods"
            ][0]["foodName"],
        )
        self.assertEqual(
            1_300,
            cached_hydration["hydration"]["valueInML"],
        )
        self.assertEqual(new_food, cached_nutrition["food_log"])
        api.get_hydration_data.assert_called_once_with(day_string)
        api.get_nutrition_daily_food_log.assert_called_once_with(day_string)
        api.get_nutrition_daily_meals.assert_called_once_with(day_string)

    def test_repeated_identical_failures_are_counted_per_call(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            exporter = self.exporter(
                root,
                ExportCache(root, enabled=False, cache_dir=root / ".cache"),
                Mock(),
                date(2026, 1, 11),
            )

            exporter._record_endpoint_failure(
                "Activities",
                "get_activity",
                "RuntimeError",
            )
            exporter._record_endpoint_failure(
                "Activities",
                "get_activity",
                "RuntimeError",
            )

        self.assertEqual(2, exporter._endpoint_failure_count())
        self.assertEqual(1, len(exporter.endpoint_failures))

    def test_fresh_global_gear_cache_avoids_unnecessary_calls(self):
        export_date = date(2026, 7, 29)
        api = Mock()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = ExportCache(root, enabled=True)
            cache.put_section("gear", {
                "gear_list": [{"uuid": "cached-gear"}],
                "gear_defaults": [],
                "gear_details": [],
            })
            exporter = GarminExporter(
                api=api,
                out_dir=root,
                days=1,
                max_activities=10,
                cache=cache,
                explicit_start_date=export_date,
                explicit_end_date=export_date,
                report_type="preparation",
            )

            exporter.export_gear()

        api.get_user_profile.assert_not_called()

    def test_failed_stale_gear_refresh_preserves_complete_cache(self):
        export_date = date(2026, 7, 29)
        cached_data = {
            "gear_list": [{"uuid": "cached-gear"}],
            "gear_defaults": [],
            "gear_details": [],
        }
        api = Mock()
        api.get_user_profile.side_effect = RuntimeError("dato privado")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = ExportCache(root, enabled=True)
            cache.put_section("gear", cached_data)
            cache_path = cache.section_dir / "gear.json"
            os.utime(cache_path, (0, 0))
            exporter = GarminExporter(
                api=api,
                out_dir=root,
                days=1,
                max_activities=10,
                cache=cache,
                explicit_start_date=export_date,
                explicit_end_date=export_date,
                report_type="preparation",
            )

            exporter.export_gear()
            preserved = cache.get_section("gear")

        api.get_user_profile.assert_called_once_with()
        self.assertEqual(cached_data, preserved)

    def test_silent_invalid_gear_list_preserves_complete_cache(self):
        export_date = date(2026, 7, 29)
        cached_data = {
            "gear_list": [{"uuid": "cached-gear"}],
            "gear_defaults": [],
            "gear_details": [],
        }
        for invalid_payload in (None, {"unexpected": "shape"}):
            with self.subTest(payload=invalid_payload):
                api = Mock()
                api.get_user_profile.return_value = {"id": 123}
                api.get_gear.return_value = invalid_payload
                api.get_gear_defaults.return_value = []
                with tempfile.TemporaryDirectory() as directory:
                    root = Path(directory)
                    cache = ExportCache(root, enabled=True)
                    cache.put_section("gear", cached_data)
                    os.utime(cache.section_dir / "gear.json", (0, 0))
                    exporter = GarminExporter(
                        api=api,
                        out_dir=root,
                        days=1,
                        max_activities=10,
                        cache=cache,
                        explicit_start_date=export_date,
                        explicit_end_date=export_date,
                        report_type="preparation",
                    )

                    exporter.export_gear()
                    preserved = cache.get_section("gear")

                self.assertEqual(cached_data, preserved)

    def test_empty_gear_list_is_a_valid_complete_refresh(self):
        export_date = date(2026, 7, 29)
        api = Mock()
        api.get_user_profile.return_value = {"id": 123}
        api.get_gear.return_value = []
        api.get_gear_defaults.return_value = []
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = ExportCache(root, enabled=True)
            cache.put_section("gear", {
                "gear_list": [{"uuid": "old-gear"}],
                "gear_defaults": [],
                "gear_details": [],
            })
            os.utime(cache.section_dir / "gear.json", (0, 0))
            exporter = GarminExporter(
                api=api,
                out_dir=root,
                days=1,
                max_activities=10,
                cache=cache,
                explicit_start_date=export_date,
                explicit_end_date=export_date,
                report_type="preparation",
            )

            exporter.export_gear()
            refreshed = cache.get_section("gear")

        self.assertEqual([], refreshed["gear_list"])

    def test_successful_stale_gear_refresh_is_reused(self):
        export_date = date(2026, 7, 29)
        api = Mock()
        api.get_user_profile.return_value = {"id": 123}
        api.get_gear.return_value = [{"uuid": "fresh-gear"}]
        api.get_gear_defaults.return_value = []
        api.get_gear_stats.return_value = {"totalDistance": 10_000}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = ExportCache(root, enabled=True)
            cache.put_section("gear", {
                "gear_list": [{"uuid": "old-gear"}],
                "gear_defaults": [],
                "gear_details": [],
            })
            os.utime(cache.section_dir / "gear.json", (0, 0))
            exporter = GarminExporter(
                api=api,
                out_dir=root,
                days=1,
                max_activities=10,
                cache=cache,
                explicit_start_date=export_date,
                explicit_end_date=export_date,
                report_type="preparation",
            )

            exporter.export_gear()
            api.reset_mock()
            second_exporter = GarminExporter(
                api=api,
                out_dir=root,
                days=1,
                max_activities=10,
                cache=cache,
                explicit_start_date=export_date,
                explicit_end_date=export_date,
                report_type="preparation",
            )
            second_exporter.export_gear()
            refreshed = cache.get_section("gear")

        api.get_user_profile.assert_not_called()
        self.assertEqual(
            "fresh-gear",
            refreshed["gear_list"][0]["uuid"],
        )


class ActivityReferenceContractTests(unittest.TestCase):
    def test_all_generated_activity_references_use_private_hex_pattern(self):
        raw_activities = (
            {
                "activityId": 987_654_321,
                "activityType": {"typeKey": "running"},
                "startTimeLocal": "2026-01-11 08:00:00",
            },
            {
                "activityId": "server-uuid-with-private-shape",
                "activityType": {"typeKey": "cycling"},
                "startTimeLocal": "2026-01-10 18:00:00",
            },
        )

        generated_references = []
        for raw_activity in raw_activities:
            generated_references.append(
                activity_catalog_entry(
                    raw_activity,
                    REFERENCE_SECRET,
                )["activity_ref"]
            )
            generated_references.append(
                _compact_activity(
                    {"summary": raw_activity},
                    reference_secret=REFERENCE_SECRET,
                )["activity_ref"]
            )

        self.assertEqual(4, len(generated_references))
        for reference in generated_references:
            self.assertIsNotNone(
                ACTIVITY_REFERENCE_PATTERN.fullmatch(reference),
                reference,
            )
            self.assertNotIn("987654321", reference)
            self.assertNotIn("server-uuid", reference)


if __name__ == "__main__":
    unittest.main()
