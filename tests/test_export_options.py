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
    _chunked_date_call,
    _normalise_output_filename,
)


class OutputFilenameTests(unittest.TestCase):
    def test_adds_txt_extension(self):
        self.assertEqual(
            "garmin_datos_2026-01-01_a_2026-01-31.txt",
            _normalise_output_filename(
                "garmin_datos_2026-01-01_a_2026-01-31"
            ),
        )

    def test_rejects_paths(self):
        with self.assertRaisesRegex(ValueError, "sin carpetas"):
            _normalise_output_filename(r"otra\carpeta\datos.txt")


class DateChunkTests(unittest.TestCase):
    def setUp(self):
        self.previous_limiter = garmin_export._limiter
        garmin_export._limiter = RateLimiter(base_delay=0)

    def tearDown(self):
        garmin_export._limiter = self.previous_limiter

    def test_single_day_range_still_calls_api(self):
        api_call = Mock(return_value=[{"date": "2026-01-10"}])

        result = _chunked_date_call(
            api_call,
            date(2026, 1, 10),
            date(2026, 1, 10),
            "single_day",
        )

        api_call.assert_called_once_with("2026-01-10", "2026-01-10")
        self.assertEqual([{"date": "2026-01-10"}], result)


class BloodPressureTests(unittest.TestCase):
    def setUp(self):
        self.previous_compact_mode = garmin_export._compact_mode
        self.previous_limiter = garmin_export._limiter
        garmin_export._compact_mode = True
        garmin_export._limiter = RateLimiter(base_delay=0)

    def tearDown(self):
        garmin_export._compact_mode = self.previous_compact_mode
        garmin_export._limiter = self.previous_limiter

    def test_exports_selected_blood_pressure_range(self):
        api = Mock()
        api.get_blood_pressure.return_value = {
            "measurementSummaries": [
                {"systolic": 120, "diastolic": 75}
            ]
        }
        exporter = GarminExporter(
            api=api,
            out_dir=Path("export"),
            days=30,
            max_activities=100,
            explicit_start_date=date(2026, 1, 10),
            explicit_end_date=date(2026, 1, 17),
        )

        exporter.export_blood_pressure()

        api.get_blood_pressure.assert_called_once_with(
            "2026-01-10", "2026-01-17"
        )
        self.assertIn("Blood Pressure", "\n".join(exporter.md))
        self.assertIn('"systolic_mmhg": 120', "\n".join(exporter.md))


class ActivityDetailsTests(unittest.TestCase):
    def setUp(self):
        self.previous_compact_mode = garmin_export._compact_mode
        self.previous_limiter = garmin_export._limiter
        garmin_export._compact_mode = True
        garmin_export._limiter = RateLimiter(base_delay=0)

    def tearDown(self):
        garmin_export._compact_mode = self.previous_compact_mode
        garmin_export._limiter = self.previous_limiter

    def _export_activity(self, include_details):
        api = Mock()
        activity = {
            "activityId": 42,
            "activityName": "Tirada larga",
            "activityType": {"typeKey": "running"},
            "startTimeLocal": "2026-01-12 08:00:00",
        }
        api.get_activities_by_date.return_value = [activity]

        with tempfile.TemporaryDirectory() as temp_dir:
            cache = ExportCache(Path(temp_dir), enabled=True)
            cache.put_activity(
                42,
                {
                    "summary": activity,
                    "detail": {"distance": 21097},
                    "details": {
                        "metricDescriptors": [
                            {
                                "key": "directHeartRate",
                                "metricsIndex": 0,
                                "unit": {"key": "bpm", "factor": 1.0},
                            },
                            {
                                "key": "directLatitude",
                                "metricsIndex": 1,
                                "unit": {"key": "degree", "factor": 1.0},
                            }
                        ],
                        "activityDetailMetrics": [
                            {"metrics": [150, 39.0], "offset": 0},
                            {"metrics": [151, 39.1], "offset": 1},
                        ],
                    },
                },
            )
            exporter = GarminExporter(
                api=api,
                out_dir=Path(temp_dir),
                days=30,
                max_activities=100,
                explicit_start_date=date(2026, 1, 10),
                explicit_end_date=date(2026, 1, 17),
                cache=cache,
                include_activity_details=include_details,
            )
            exporter.export_activities()
            output = "\n".join(exporter.md)

        data_line = next(
            line for line in output.splitlines()
            if line.startswith("[{")
        )
        return json.loads(data_line)[0]

    def test_compact_mode_omits_activity_details_by_default(self):
        self.assertNotIn("activity_series", self._export_activity(False))

    def test_compact_mode_can_include_activity_details(self):
        activity = self._export_activity(True)
        self.assertIn("activity_series", activity)
        series = activity["activity_series"]
        self.assertEqual(1, len(series["metric_descriptors"]))
        self.assertEqual(1, len(series["samples"][0]))
        self.assertNotIn("Latitude", json.dumps(series))


if __name__ == "__main__":
    unittest.main()
