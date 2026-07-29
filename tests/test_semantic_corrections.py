import json
import tempfile
import unittest
from datetime import date, datetime
from pathlib import Path
from unittest.mock import Mock, patch

import garmin_export
from garmin_export import (
    ExportCache,
    GarminExporter,
    RateLimiter,
    _compact_activity,
    _compact_activity_series,
    _compact_daily_record,
    _compact_training,
    _json,
    _normalise_lactate_speed,
    _normalise_laps,
    _normalise_self_evaluation,
    _normalise_temperature,
    _resolve_timezone,
    _strip_empty,
    _timezone_metadata,
    _weekly_summary,
)


def activity_series_fixture():
    return {
        "metricDescriptors": [
            {"metricsIndex": 0, "key": "directTimestamp", "unit": {"key": "gmt"}},
            {"metricsIndex": 1, "key": "directHeartRate", "unit": {"key": "bpm"}},
            {"metricsIndex": 2, "key": "sumDuration", "unit": {"key": "second"}},
            {"metricsIndex": 3, "key": "directSpeed", "unit": {"key": "mps"}},
        ],
        "activityDetailMetrics": [
            {"metrics": [1000.0, None, 0.0, 2.0]},
            {"metrics": [2000.0, 80.0, 10.0, None]},
            {"metrics": [3000.0, None, 20.0, 2.2]},
            {"metrics": [4000.0, 120.0, 30.0, 2.3]},
        ],
    }


def compact_activity_fixture(activity_id=1, include_series=True):
    return {
        "summary": {
            "activityId": activity_id,
            "activityName": "Actividad anónima",
            "activityType": {"typeKey": "running"},
            "startTimeLocal": "2026-07-06 08:00:00",
            "distance": 3010.12345,
            "duration": 30.12345,
            "averageSpeed": 2.5,
            "averageHR": 100,
        },
        "detail": {
            "summaryDTO": {
                "directWorkoutRpe": 10,
                "directWorkoutFeel": 50,
            }
        },
        "splits": {
            "lapDTOs": [
                {"lapIndex": 1, "distance": 1000, "duration": 10, "intensityType": "ACTIVE"},
                {"lapIndex": 2, "distance": 1001, "duration": 10, "intensityType": "ACTIVE"},
                {"lapIndex": 3, "distance": 999, "duration": 10, "intensityType": "ACTIVE"},
                {"lapIndex": 4, "distance": 10, "duration": 0.1, "intensityType": "ACTIVE"},
            ]
        },
        "hr_zones": [
            {"zoneNumber": 1, "secsInZone": 10, "zoneLowBoundary": 90}
        ],
        "details": activity_series_fixture() if include_series else None,
        "gear": [{
            "uuid": "equipo-1",
            "displayName": "Zapatillas anónimas",
            "gearMakeName": "Saucony",
            "gearModelName": "Endorphin Speed 4",
        }],
    }


class PositionalSeriesTests(unittest.TestCase):
    def test_nulls_in_positional_arrays_are_not_removed(self):
        cleaned = _strip_empty({"samples": [[None, 1, None]]})
        self.assertEqual([[None, 1, None]], cleaned["samples"])

    def test_all_samples_match_descriptor_length(self):
        series = _compact_activity_series(activity_series_fixture())
        expected = len(series["metric_descriptors"])
        self.assertTrue(all(len(row) == expected for row in series["samples"]))

    def test_initial_null_does_not_shift_columns(self):
        series = _compact_activity_series(activity_series_fixture())
        self.assertEqual([None, 0.0, 2.0], series["samples"][0])

    def test_intermediate_null_does_not_shift_columns(self):
        series = _compact_activity_series(activity_series_fixture())
        self.assertEqual([80.0, 10.0, None], series["samples"][1])

    def test_invalid_row_is_omitted_and_reported(self):
        fixture = activity_series_fixture()
        fixture["activityDetailMetrics"].insert(2, {"notMetrics": []})
        diagnostics = []
        series = _compact_activity_series(fixture, diagnostics)
        self.assertIsNotNone(series)
        self.assertEqual(4, len(series["samples"]))
        self.assertEqual([None, 20.0, 2.2], series["samples"][2])
        self.assertTrue(diagnostics)

    def test_negative_descriptor_index_is_never_used(self):
        fixture = activity_series_fixture()
        fixture["metricDescriptors"].insert(1, {
            "metricsIndex": -1,
            "key": "directHeartRate",
            "unit": {"key": "bpm"},
        })
        diagnostics = []

        series = _compact_activity_series(fixture, diagnostics)

        self.assertEqual(
            ["heart_rate_raw", "duration_raw", "speed_raw"],
            [
                descriptor["field"]
                for descriptor in series["metric_descriptors"]
            ],
        )
        self.assertEqual([None, 0.0, 2.0], series["samples"][0])
        self.assertTrue(any("índice no válido" in item for item in diagnostics))


class UnitConversionTests(unittest.TestCase):
    def test_sleep_need_minutes_are_converted_to_seconds(self):
        record = _compact_daily_record("2026-07-06", {
            "sleep": {"dailySleepDTO": {"sleepNeed": {"actual": 420}}}
        })
        self.assertEqual(25200, record["sleep"]["sleep_need_s"])

    def test_sleep_need_out_of_range_is_not_falsely_normalised(self):
        record = _compact_daily_record("2026-07-06", {
            "sleep": {"dailySleepDTO": {"sleepNeed": {"actual": 2000}}}
        })
        self.assertNotIn("sleep_need_s", record["sleep"])
        self.assertEqual(2000, record["sleep"]["sleep_need_raw"])

    def test_example_lactate_speed_is_about_330_seconds_per_km(self):
        value = _normalise_lactate_speed(0.30277693, "garmin_tenths_m_s")
        self.assertAlmostEqual(3.0278, value["speed_m_s"], places=4)
        self.assertAlmostEqual(330.3, value["pace_s_per_km"], places=1)

    def test_unknown_lactate_unit_does_not_create_false_pace(self):
        value = _normalise_lactate_speed(0.30277693, "unknown")
        self.assertEqual(0.30277693, value["speed_raw"])
        self.assertNotIn("speed_m_s", value)
        self.assertNotIn("pace_s_per_km", value)

    def test_fahrenheit_weather_is_converted_to_celsius(self):
        value = _normalise_temperature(75, "fahrenheit")
        self.assertAlmostEqual(23.9, value["temperature_c"], places=1)

    def test_celsius_sensor_is_not_converted_again(self):
        value = _normalise_temperature(23.9, "celsius")
        self.assertEqual(23.9, value["temperature_c"])

    def test_unknown_temperature_stays_raw(self):
        value = _normalise_temperature(75, "unknown")
        self.assertNotIn("temperature_c", value)
        self.assertEqual(75, value["temperature_raw"])


class EvaluationAndHeartRateTests(unittest.TestCase):
    def test_rpe_raw_10_maps_to_one(self):
        result = _normalise_self_evaluation({
            "directWorkoutRpe": 10,
            "directWorkoutFeel": 50,
        })
        self.assertEqual(1, result["perceived_exertion_1_10"])
        self.assertEqual("normal", result["feeling"])

    def test_rpe_raw_50_and_100_map_to_five_and_ten(self):
        five = _normalise_self_evaluation({"directWorkoutRpe": 50})
        ten = _normalise_self_evaluation({"directWorkoutRpe": 100})
        self.assertEqual(5, five["perceived_exertion_1_10"])
        self.assertEqual(10, ten["perceived_exertion_1_10"])

    def test_missing_evaluation_is_not_counted(self):
        result = _weekly_summary(
            [{"sport": "running", "duration_s": 10}],
            [],
        )
        self.assertEqual(
            0,
            result["weekly_summary"]["self_evaluated_activities"],
        )

    def test_default_zero_pair_is_not_counted(self):
        self.assertIsNone(_normalise_self_evaluation({
            "directWorkoutRpe": 0,
            "directWorkoutFeel": 0,
        }))

    def test_zone_zero_only_contains_valid_hr_below_zone_one(self):
        result = _compact_activity(compact_activity_fixture())
        zone_zero = next(zone for zone in result["hr_zones"] if zone["zone"] == 0)
        self.assertEqual(10, zone_zero["duration_s"])

    def test_missing_hr_gaps_are_not_assigned_to_zone_zero(self):
        result = _compact_activity(compact_activity_fixture())
        quality = result["heart_rate_distribution_quality"]
        self.assertEqual(10, quality["missing_heart_rate_duration_s"])
        zone_zero = next(zone for zone in result["hr_zones"] if zone["zone"] == 0)
        self.assertEqual(10, zone_zero["duration_s"])

    def test_zone_percentages_sum_to_about_one_hundred(self):
        result = _compact_activity(compact_activity_fixture())
        total = sum(zone["percentage"] for zone in result["hr_zones"])
        self.assertAlmostEqual(100, total, places=6)

    def test_fully_classified_activity_has_zero_seconds_in_zone_zero(self):
        fixture = compact_activity_fixture()
        fixture["hr_zones"][0]["secsInZone"] = 20
        result = _compact_activity(fixture)
        zone_zero = next(zone for zone in result["hr_zones"] if zone["zone"] == 0)
        self.assertEqual(0, zone_zero["duration_s"])
        self.assertEqual(
            100,
            result["heart_rate_distribution_quality"][
                "heart_rate_zone_coverage_pct"
            ],
        )

    def test_activity_without_hr_has_no_zone_zero(self):
        fixture = compact_activity_fixture(include_series=False)
        fixture["summary"].pop("averageHR")
        fixture["hr_zones"] = []
        result = _compact_activity(fixture)
        self.assertFalse(result.get("hr_zones"))
        self.assertNotIn("heart_rate_distribution_quality", result)

    def test_weekly_distribution_combines_multiple_activities(self):
        first = _compact_activity(compact_activity_fixture(1))
        second = _compact_activity(compact_activity_fixture(2))
        result = _weekly_summary([first, second], [])
        self.assertAlmostEqual(
            100,
            sum(
                zone["percentage"]
                for zone in result["heart_rate_distribution"]
            ),
            places=6,
        )
        self.assertEqual(
            20,
            result["heart_rate_distribution_quality"][
                "missing_heart_rate_duration_s"
            ],
        )


class TimezoneAndVariantTests(unittest.TestCase):
    def test_windows_timezone_resolves_to_europe_madrid(self):
        with patch("garmin_export._windows_timezone_key", return_value="Romance Standard Time"):
            self.assertEqual("Europe/Madrid", _resolve_timezone())

    def test_summer_and_winter_offsets_are_historical(self):
        self.assertEqual(
            "+01:00",
            _timezone_metadata("Europe/Madrid", date(2026, 1, 15))[1],
        )
        self.assertEqual(
            "+02:00",
            _timezone_metadata("Europe/Madrid", date(2026, 7, 15))[1],
        )

    def test_dst_change_uses_requested_historical_date(self):
        before = _timezone_metadata("Europe/Madrid", date(2026, 3, 28))[1]
        after = _timezone_metadata("Europe/Madrid", date(2026, 3, 30))[1]
        self.assertEqual(("+01:00", "+02:00"), (before, after))

    def test_short_variant_omits_series(self):
        result = _compact_activity(compact_activity_fixture(), include_series=False)
        self.assertNotIn("activity_series", result)

    def test_complete_variant_contains_series(self):
        result = _compact_activity(compact_activity_fixture(), include_series=True)
        self.assertIn("activity_series", result)

    def test_metadata_distinguishes_series_mode(self):
        previous = garmin_export._compact_mode
        garmin_export._compact_mode = True
        try:
            exporter = GarminExporter(
                api=Mock(),
                out_dir=Path("export"),
                days=1,
                max_activities=1,
                explicit_start_date=date(2026, 7, 6),
                explicit_end_date=date(2026, 7, 12),
                include_activity_details=True,
                timezone_name="Europe/Madrid",
            )
            exporter.export_metadata()
        finally:
            garmin_export._compact_mode = previous
        text = "\n".join(exporter.md)
        self.assertIn('"activity_series_mode": "full"', text)
        self.assertIn('"timezone": "Europe/Madrid"', text)
        self.assertIn('"utc_offset": "+02:00"', text)


class RegressionTests(unittest.TestCase):
    def test_four_activities_remain(self):
        activities = [
            _compact_activity(compact_activity_fixture(index))
            for index in range(1, 5)
        ]
        self.assertEqual(4, len(activities))

    def test_laps_remain_and_partial_last_lap_is_marked(self):
        laps, _ = _normalise_laps(compact_activity_fixture())
        self.assertEqual(4, len(laps))
        self.assertTrue(laps[-1]["partial_lap"])

    def test_structured_short_interval_is_not_marked_partial(self):
        fixture = compact_activity_fixture()
        fixture["splits"]["lapDTOs"][-1]["intensityType"] = "RECOVERY"
        laps, _ = _normalise_laps(fixture)
        self.assertNotIn("partial_lap", laps[-1])

    def test_gear_remains_associated(self):
        result = _compact_activity(compact_activity_fixture())
        gear = result["gear"][0]
        self.assertRegex(gear["gear_ref"], r"^gear_[0-9a-f]{12}$")
        self.assertEqual("Zapatillas anónimas", gear["gear_name"])
        self.assertEqual("Saucony", gear["manufacturer"])
        self.assertEqual("Endorphin Speed 4", gear["model"])
        self.assertNotIn("custom_name", gear)

    def test_future_metrics_stay_in_current_snapshot(self):
        result, _ = _compact_training(
            {"race_predictions": {"calendarDate": "2026-07-20", "time5K": 1200}},
            date(2026, 7, 6),
            date(2026, 7, 12),
        )
        self.assertIn("race_predictions", result["current_snapshot"])

    def test_sleep_and_hrv_failure_does_not_stop_export(self):
        api = Mock()
        methods = [
            "get_user_summary", "get_heart_rates", "get_rhr_day",
            "get_all_day_stress", "get_spo2_data", "get_respiration_data",
            "get_body_battery", "get_body_battery_events",
            "get_intensity_minutes_data", "get_all_day_events",
            "get_lifestyle_logging_data",
        ]
        for method in methods:
            getattr(api, method).return_value = None
        api.get_sleep_data.side_effect = RuntimeError("sin datos")
        api.get_hrv_data.side_effect = RuntimeError("sin datos")

        previous_compact = garmin_export._compact_mode
        previous_limiter = garmin_export._limiter
        garmin_export._compact_mode = True
        garmin_export._limiter = RateLimiter(base_delay=0)
        try:
            with tempfile.TemporaryDirectory() as temp_dir:
                exporter = GarminExporter(
                    api=api,
                    out_dir=Path(temp_dir),
                    days=1,
                    max_activities=1,
                    explicit_start_date=date(2026, 7, 6),
                    explicit_end_date=date(2026, 7, 6),
                    cache=ExportCache(Path(temp_dir), enabled=False),
                    timezone_name="Europe/Madrid",
                )
                exporter.export_daily_health()
        finally:
            garmin_export._compact_mode = previous_compact
            garmin_export._limiter = previous_limiter
        self.assertTrue(exporter.compact_daily_records)
        self.assertTrue(exporter.data_quality["missing_critical_data"])

    def test_rounding_happens_only_in_compact_serialisation(self):
        previous = garmin_export._compact_mode
        garmin_export._compact_mode = True
        try:
            raw = {"distance_m": 33552.98828125, "duration_s": 8151.15380859375}
            encoded = json.loads(_json(raw))
        finally:
            garmin_export._compact_mode = previous
        self.assertEqual(33553.0, encoded["distance_m"])
        self.assertEqual(8151.2, encoded["duration_s"])
        self.assertEqual(33552.98828125, raw["distance_m"])


if __name__ == "__main__":
    unittest.main()
