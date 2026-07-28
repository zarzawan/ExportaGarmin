import unittest
from datetime import date
from pathlib import Path

from garmin_export import GarminExporter


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


if __name__ == "__main__":
    unittest.main()
