"""Deriva descriptiva con cobertura temporal y medias ponderadas por tiempo."""
import math


def calculate_cardiac_drift(activity):
    # Importación diferida para conservar la API pública del modelo semántico.
    from training_analysis import _number, _sport_family

    def unavailable(reason, **coverage):
        return {"status": "not_eligible", "reason": reason, **coverage}

    if _sport_family(activity.get("sport")) != "running":
        return unavailable("not_running")
    series = activity.get("activity_series") or {}
    descriptors = series.get("metric_descriptors") or []
    samples = series.get("samples") or []
    if not isinstance(descriptors, list) or not isinstance(samples, list):
        return unavailable("activity_series_not_included")
    fields = [d.get("field") if isinstance(d, dict) else None for d in descriptors]
    time_field = next((f for f in ("elapsed_duration_raw", "duration_raw") if f in fields), None)
    speed_field = next((f for f in ("enhanced_speed_raw", "speed_raw") if f in fields), None)
    if not time_field or not speed_field or "heart_rate_raw" not in fields:
        return unavailable("required_series_columns_missing")
    ti, si, hi = (fields.index(f) for f in (time_field, speed_field, "heart_rate_raw"))
    unit = str(descriptors[ti].get("source_unit") or "").casefold()
    time_factor = {"second": 1, "seconds": 1, "s": 1,
                   "millisecond": .001, "milliseconds": .001, "ms": .001}.get(unit)
    if time_factor is None:
        return unavailable("duration_unit_unconfirmed")
    duration = _number(activity.get("elapsed_duration_s" if time_field == "elapsed_duration_raw" else "duration_s"))
    duration = duration or _number(activity.get("duration_s")) or 0
    if duration < 1800:
        return unavailable("duration_below_30_minutes")
    points, valid_count, previous_time = [], 0, None
    for row in samples:
        if not isinstance(row, list) or len(row) != len(fields):
            points.append(None)
            continue
        elapsed, speed, heart = (_number(row[i]) for i in (ti, si, hi))
        if elapsed is None:
            points.append(None)
            continue
        elapsed *= time_factor
        if elapsed < 0 or elapsed > duration + 1:
            return unavailable("time_outside_activity")
        if previous_time is not None and elapsed <= previous_time:
            return unavailable("non_increasing_timestamps")
        previous_time = elapsed
        if speed is None or speed <= 0 or heart is None or heart <= 0:
            points.append(None)
            continue
        points.append((elapsed, speed, heart))
        valid_count += 1
    row_coverage = 100 * valid_count / len(samples) if samples else 0
    # Integración trapezoidal; nunca rellenar ausencias o huecos > 30 segundos.
    halves = [[0., 0., 0., 0.], [0., 0., 0., 0.]]
    midpoint = duration / 2
    for left, right in zip(points, points[1:]):
        if left is None or right is None:
            continue
        dt = right[0] - left[0]
        if dt > 30:
            continue
        for index, (begin, end) in enumerate(((0., midpoint), (midpoint, duration))):
            lo, high = max(left[0], begin), min(right[0], end)
            if high <= lo:
                continue
            a, b = (lo-left[0])/dt, (high-left[0])/dt
            sa, sb = (left[1]+f*(right[1]-left[1]) for f in (a, b))
            ha, hb = (left[2]+f*(right[2]-left[2]) for f in (a, b))
            weight = high-lo
            values = (weight, weight*(sa+sb)/2, weight*(ha+hb)/2,
                      weight*(sa*sa+sa*sb+sb*sb)/3)
            halves[index] = [v+n for v, n in zip(halves[index], values)]
    covered = sum(h[0] for h in halves)
    coverage = {"series_coverage_pct": round(100*covered/duration, 1),
                "valid_rows_pct": round(row_coverage, 1),
                "first_half_coverage_pct": round(100*halves[0][0]/midpoint, 1),
                "second_half_coverage_pct": round(100*halves[1][0]/midpoint, 1)}
    if valid_count < 20 or row_coverage < 80 or any(h[0] < .8*midpoint for h in halves):
        return unavailable("insufficient_temporal_coverage", **coverage)
    mean_speed = sum(h[1] for h in halves)/covered
    variance = max(0., sum(h[3] for h in halves)/covered-mean_speed**2)
    cv = math.sqrt(variance)/mean_speed
    if cv > .15:
        return unavailable("pace_not_stable", speed_coefficient_of_variation=round(cv, 3), **coverage)
    first, second = (h[2]/h[1] for h in halves)
    return {"status": "available", "cardiac_drift_pct": round((second/first-1)*100, 1),
            "formula": "((FC/velocidad segunda mitad) / (FC/velocidad primera mitad) - 1) × 100",
            "method": "Medias ponderadas por tiempo mediante integración trapezoidal; hueco máximo 30 s.",
            "speed_coefficient_of_variation": round(cv, 3), **coverage,
            "limitations": "Descripción fisiológica, no diagnóstico. Exige al menos 80 % de cobertura temporal en cada mitad; no interpola huecos largos."}
