import json
import math
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import Mock, patch

import garmin_export
from garmin_export import (
    ExportCache,
    GarminExporter,
    _compact_daily_record,
    _compact_sleep,
    _json,
    epoch_ms_to_iso,
)


def utc_epoch_ms(iso_value):
    return round(
        datetime.fromisoformat(iso_value)
        .astimezone(timezone.utc)
        .timestamp()
        * 1000
    )


class EpochMillisecondsTests(unittest.TestCase):
    def test_converts_garmin_epoch_milliseconds(self):
        self.assertEqual(
            "2026-07-24T03:45:01+02:00",
            epoch_ms_to_iso(1784857501000, "Europe/Madrid"),
        )

    def test_uses_europe_madrid(self):
        value = utc_epoch_ms("2026-07-24T00:00:00+00:00")
        self.assertEqual(
            "2026-07-24T02:00:00+02:00",
            epoch_ms_to_iso(value, "Europe/Madrid"),
        )

    def test_summer_offset_is_plus_two(self):
        value = utc_epoch_ms("2026-07-24T12:00:00+00:00")
        self.assertTrue(
            epoch_ms_to_iso(value, "Europe/Madrid").endswith("+02:00")
        )

    def test_winter_offset_is_plus_one(self):
        value = utc_epoch_ms("2026-01-24T12:00:00+00:00")
        self.assertTrue(
            epoch_ms_to_iso(value, "Europe/Madrid").endswith("+01:00")
        )

    def test_spring_dst_change(self):
        before = utc_epoch_ms("2026-03-29T00:30:00+00:00")
        after = utc_epoch_ms("2026-03-29T01:30:00+00:00")
        self.assertEqual(
            "2026-03-29T01:30:00+01:00",
            epoch_ms_to_iso(before, "Europe/Madrid"),
        )
        self.assertEqual(
            "2026-03-29T03:30:00+02:00",
            epoch_ms_to_iso(after, "Europe/Madrid"),
        )

    def test_autumn_dst_change(self):
        before = utc_epoch_ms("2026-10-25T00:30:00+00:00")
        after = utc_epoch_ms("2026-10-25T01:30:00+00:00")
        self.assertEqual(
            "2026-10-25T02:30:00+02:00",
            epoch_ms_to_iso(before, "Europe/Madrid"),
        )
        self.assertEqual(
            "2026-10-25T02:30:00+01:00",
            epoch_ms_to_iso(after, "Europe/Madrid"),
        )

    def test_null_is_omitted(self):
        self.assertIsNone(epoch_ms_to_iso(None, "Europe/Madrid"))

    def test_non_numeric_value_is_omitted(self):
        self.assertIsNone(epoch_ms_to_iso("1784857501000", "Europe/Madrid"))

    def test_nan_and_infinity_are_omitted(self):
        self.assertIsNone(epoch_ms_to_iso(math.nan, "Europe/Madrid"))
        self.assertIsNone(epoch_ms_to_iso(math.inf, "Europe/Madrid"))

    def test_negative_and_unreasonable_values_are_omitted(self):
        self.assertIsNone(epoch_ms_to_iso(-1000, "Europe/Madrid"))
        self.assertIsNone(epoch_ms_to_iso(999999999999999, "Europe/Madrid"))
        self.assertIsNone(epoch_ms_to_iso(10 ** 1000, "Europe/Madrid"))

    def test_seconds_are_not_silently_interpreted_as_milliseconds(self):
        self.assertIsNone(epoch_ms_to_iso(1784857501, "Europe/Madrid"))
        self.assertIsNotNone(epoch_ms_to_iso(1784857501000, "Europe/Madrid"))

    def test_conversion_explicitly_starts_in_utc(self):
        with patch("garmin_export.datetime", wraps=datetime) as mocked_datetime:
            epoch_ms_to_iso(1784857501000, "Europe/Madrid")
        self.assertEqual(
            timezone.utc,
            mocked_datetime.fromtimestamp.call_args.kwargs["tz"],
        )

    def test_iso_always_has_offset_and_not_z(self):
        result = epoch_ms_to_iso(1784857501000, "Europe/Madrid")
        self.assertRegex(result, r"[+-]\d{2}:\d{2}$")
        self.assertFalse(result.endswith("Z"))


class CompactSleepTimestampTests(unittest.TestCase):
    def sleep_fixture(self):
        return {
            "dailySleepDTO": {
                "calendarDate": "2026-07-24",
                "sleepStartTimestampLocal": 1784857501000,
                "sleepEndTimestampLocal": 1784881561000,
                "sleepTimeSeconds": 23100,
                "awakeSleepSeconds": 960,
                "deepSleepSeconds": 3600,
            }
        }

    def test_sleep_start_is_before_sleep_end(self):
        result = _compact_sleep(self.sleep_fixture(), "Europe/Madrid")
        start = datetime.fromisoformat(result["sleep_start_local"])
        end = datetime.fromisoformat(result["sleep_end_local"])
        self.assertLess(start, end)

    def test_sleep_window_matches_sleep_plus_awake(self):
        result = _compact_sleep(self.sleep_fixture(), "Europe/Madrid")
        start = datetime.fromisoformat(result["sleep_start_local"])
        end = datetime.fromisoformat(result["sleep_end_local"])
        self.assertEqual(
            (end - start).total_seconds(),
            result["total_sleep_s"] + result["awake_s"],
        )

    def test_compact_local_fields_are_not_numeric_epochs(self):
        result = _compact_daily_record(
            "2026-07-24",
            {"sleep": self.sleep_fixture()},
            timezone_name="Europe/Madrid",
        )

        def assert_local_fields(value):
            if isinstance(value, dict):
                for key, child in value.items():
                    if key.endswith("_local"):
                        self.assertIsInstance(child, str)
                    assert_local_fields(child)
            elif isinstance(value, list):
                for child in value:
                    assert_local_fields(child)

        assert_local_fields(result)

    def test_invalid_epoch_adds_non_blocking_quality_warning(self):
        warnings = []
        result = _compact_sleep(
            {
                "dailySleepDTO": {
                    "sleepStartTimestampLocal": "incorrecto",
                    "sleepEndTimestampLocal": 1784881561000,
                    "sleepTimeSeconds": 100,
                }
            },
            timezone_name="Europe/Madrid",
            day_string="2026-07-24",
            quality_callback=lambda category, message: warnings.append(
                (category, message)
            ),
        )
        self.assertNotIn("sleep_start_local", result)
        self.assertIn("sleep_end_local", result)
        self.assertEqual("temporal_warnings", warnings[0][0])
        self.assertIn("sleep_start_local", warnings[0][1])
        self.assertIn("2026-07-24", warnings[0][1])

    def test_incoherent_window_warns_but_keeps_values(self):
        warnings = []
        fixture = self.sleep_fixture()
        fixture["dailySleepDTO"]["sleepTimeSeconds"] = 100
        fixture["dailySleepDTO"]["awakeSleepSeconds"] = 0
        result = _compact_sleep(
            fixture,
            timezone_name="Europe/Madrid",
            day_string="2026-07-24",
            quality_callback=lambda category, message: warnings.append(
                (category, message)
            ),
        )
        self.assertIn("sleep_start_local", result)
        self.assertIn("sleep_end_local", result)
        self.assertTrue(
            any("cinco minutos" in message for _, message in warnings)
        )

    def test_serialized_compact_dates_are_iso_strings(self):
        previous = garmin_export._compact_mode
        garmin_export._compact_mode = True
        try:
            result = _compact_daily_record(
                "2026-07-24",
                {"sleep": self.sleep_fixture()},
                timezone_name="Europe/Madrid",
            )
            encoded = json.loads(_json(result))
        finally:
            garmin_export._compact_mode = previous
        self.assertEqual(
            "2026-07-24T03:45:01+02:00",
            encoded["sleep"]["sleep_start_local"],
        )

    def test_raw_mode_preserves_original_epoch(self):
        previous = garmin_export._compact_mode
        garmin_export._compact_mode = False
        try:
            with tempfile.TemporaryDirectory() as temp_dir:
                output = Path(temp_dir)
                cache = ExportCache(output, enabled=True)
                cache.put_day("2026-07-24", {"sleep": self.sleep_fixture()})
                exporter = GarminExporter(
                    api=Mock(),
                    out_dir=output,
                    days=1,
                    max_activities=1,
                    explicit_start_date=datetime(2026, 7, 24).date(),
                    explicit_end_date=datetime(2026, 7, 24).date(),
                    cache=cache,
                    timezone_name="Europe/Madrid",
                )
                exporter.export_daily_health()
                rendered = "\n".join(exporter.md)
        finally:
            garmin_export._compact_mode = previous
        self.assertIn('"sleepStartTimestampLocal": 1784857501000', rendered)


if __name__ == "__main__":
    unittest.main()
