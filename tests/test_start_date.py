import unittest
from datetime import date
from pathlib import Path

from garmin_export import GarminExporter, _inclusive_review_start


class ExplicitDateRangeTests(unittest.TestCase):
    def test_selected_date_range_is_inclusive(self):
        start_date = date(2026, 1, 10)
        end_date = date(2026, 1, 17)

        exporter = GarminExporter(
            api=object(),
            out_dir=Path("export"),
            days=30,
            max_activities=100,
            explicit_start_date=start_date,
            explicit_end_date=end_date,
        )

        self.assertEqual(start_date, exporter.start_date)
        self.assertEqual(end_date, exporter.today)
        self.assertEqual(8, exporter.days)

    def test_end_date_cannot_precede_start_date(self):
        with self.assertRaisesRegex(ValueError, "fecha inicial"):
            GarminExporter(
                api=object(),
                out_dir=Path("export"),
                days=30,
                max_activities=100,
                explicit_start_date=date(2026, 1, 18),
                explicit_end_date=date(2026, 1, 17),
            )


class PreparationWindowTests(unittest.TestCase):
    def test_review_window_contains_exactly_the_requested_weeks(self):
        end_date = date(2026, 7, 29)

        for weeks in (12, 16):
            with self.subTest(weeks=weeks):
                start_date = _inclusive_review_start(end_date, weeks)
                self.assertEqual(weeks * 7, (end_date - start_date).days + 1)


if __name__ == "__main__":
    unittest.main()
