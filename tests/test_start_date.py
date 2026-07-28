import unittest
from datetime import date, timedelta
from pathlib import Path

from garmin_export import GarminExporter


class ExplicitStartDateTests(unittest.TestCase):
    def test_selected_start_date_is_inclusive(self):
        start_date = date.today() - timedelta(days=7)

        exporter = GarminExporter(
            api=object(),
            out_dir=Path("export"),
            days=30,
            max_activities=100,
            explicit_start_date=start_date,
        )

        self.assertEqual(start_date, exporter.start_date)
        self.assertEqual(8, exporter.days)


if __name__ == "__main__":
    unittest.main()
