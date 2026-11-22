<?php
declare(strict_types=1);

/*
 * Jalali/Gregorian calendar conversion adapted from jalaali-js and the
 * Borkowski calendar algorithm. MIT attribution is in Docs/THIRD-PARTY-NOTICES.md.
 */
function jacquard_mod(int $value, int $divisor): int
{
    return $value - intdiv($value, $divisor) * $divisor;
}

function jacquard_gregorian_to_jdn(int $year, int $month, int $day): int
{
    $monthOffset = intdiv($month - 8, 6);
    $jdn = intdiv(($year + $monthOffset + 100100) * 1461, 4)
        + intdiv(153 * jacquard_mod($month + 9, 12) + 2, 5)
        + $day - 34840408;
    return $jdn - intdiv(intdiv($year + 100100 + $monthOffset, 100) * 3, 4) + 752;
}

function jacquard_jdn_to_gregorian(int $jdn): array
{
    $j = 4 * $jdn + 139361631;
    $j += intdiv(intdiv(4 * $jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
    $i = intdiv(jacquard_mod($j, 1461), 4) * 5 + 308;
    $day = intdiv(jacquard_mod($i, 153), 5) + 1;
    $month = jacquard_mod(intdiv($i, 153), 12) + 1;
    $year = intdiv($j, 1461) - 100100 + intdiv(8 - $month, 6);
    return [$year, $month, $day];
}

function jacquard_jalali_calculation(int $year): array
{
    if ($year < -61 || $year > 3177) {
        throw new OutOfRangeException('Jalali year is outside the supported range.');
    }
    $breaks = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178];
    $gregorianYear = $year + 621;
    $leapJalali = -14;
    $previousBreak = $breaks[0];
    $jump = 0;
    foreach (array_slice($breaks, 1) as $break) {
        $jump = $break - $previousBreak;
        if ($year < $break) break;
        $leapJalali += intdiv($jump, 33) * 8 + intdiv(jacquard_mod($jump, 33), 4);
        $previousBreak = $break;
    }
    $offset = $year - $previousBreak;
    $leapJalali += intdiv($offset, 33) * 8 + intdiv(jacquard_mod($offset, 33) + 3, 4);
    if (jacquard_mod($jump, 33) === 4 && $jump - $offset === 4) $leapJalali++;
    $leapGregorian = intdiv($gregorianYear, 4) - intdiv(intdiv($gregorianYear, 100) + 1, 4) * 3 - 150;
    $march = 20 + $leapJalali - $leapGregorian;

    $adjusted = $offset;
    if ($jump - $offset < 6) $adjusted = $offset - $jump + intdiv($jump + 4, 33) * 33;
    $leap = jacquard_mod(jacquard_mod($adjusted + 1, 33) - 1, 4);
    if ($leap === -1) $leap = 4;
    return ['gregorian_year' => $gregorianYear, 'march' => $march, 'leap' => $leap];
}

function jacquard_jalali_to_jdn(int $year, int $month, int $day): int
{
    $calculation = jacquard_jalali_calculation($year);
    return jacquard_gregorian_to_jdn($calculation['gregorian_year'], 3, $calculation['march'])
        + ($month - 1) * 31 - intdiv($month, 7) * ($month - 7) + $day - 1;
}

function jacquard_gregorian_to_jalali(int $year, int $month, int $day): array
{
    $jdn = jacquard_gregorian_to_jdn($year, $month, $day);
    $gregorianYear = jacquard_jdn_to_gregorian($jdn)[0];
    $jalaliYear = min($gregorianYear - 621, 3177);
    $calculation = jacquard_jalali_calculation($jalaliYear);
    $firstDay = jacquard_gregorian_to_jdn($calculation['gregorian_year'], 3, $calculation['march']);
    $offset = $jdn - $firstDay;
    if ($offset >= 0) {
        if ($offset <= 185) return [$jalaliYear, 1 + intdiv($offset, 31), jacquard_mod($offset, 31) + 1];
        $offset -= 186;
    } else {
        $jalaliYear--;
        $offset += 179;
        if ($calculation['leap'] === 1) $offset++;
    }
    return [$jalaliYear, 7 + intdiv($offset, 30), jacquard_mod($offset, 30) + 1];
}

function jacquard_jalali_is_leap(int $year): bool
{
    return jacquard_jalali_calculation($year)['leap'] === 0;
}

function jacquard_jalali_month_length(int $year, int $month): int
{
    if ($month >= 1 && $month <= 6) return 31;
    if ($month >= 7 && $month <= 11) return 30;
    if ($month === 12) return jacquard_jalali_is_leap($year) ? 30 : 29;
    return 0;
}

function jacquard_jalali_to_gregorian(int $year, int $month, int $day): ?string
{
    if ($year < -61 || $year > 3177 || $month < 1 || $month > 12 || $day < 1 || $day > jacquard_jalali_month_length($year, $month)) return null;
    [$gregorianYear, $gregorianMonth, $gregorianDay] = jacquard_jdn_to_gregorian(jacquard_jalali_to_jdn($year, $month, $day));
    return sprintf('%04d-%02d-%02d', $gregorianYear, $gregorianMonth, $gregorianDay);
}

function jacquard_latin_digits(string $value): string
{
    return strtr($value, [
        '۰'=>'0','۱'=>'1','۲'=>'2','۳'=>'3','۴'=>'4','۵'=>'5','۶'=>'6','۷'=>'7','۸'=>'8','۹'=>'9',
        '٠'=>'0','١'=>'1','٢'=>'2','٣'=>'3','٤'=>'4','٥'=>'5','٦'=>'6','٧'=>'7','٨'=>'8','٩'=>'9',
    ]);
}

function jacquard_persian_digits(string $value): string
{
    return strtr($value, ['0'=>'۰','1'=>'۱','2'=>'۲','3'=>'۳','4'=>'۴','5'=>'۵','6'=>'۶','7'=>'۷','8'=>'۸','9'=>'۹']);
}

/** Parse a user-entered Jalali date and return its Gregorian database form. */
function jacquard_parse_jalali_date(string $value): ?string
{
    $value = str_replace('-', '/', trim(jacquard_latin_digits($value)));
    if (preg_match('/\A([0-9]{4})\/([0-9]{1,2})\/([0-9]{1,2})\z/D', $value, $parts) !== 1) return null;
    $gregorian = jacquard_jalali_to_gregorian((int)$parts[1], (int)$parts[2], (int)$parts[3]);
    if ($gregorian === null || $gregorian < '2000-01-01' || $gregorian > '2100-12-30') return null;
    $roundTrip = jacquard_gregorian_to_jalali(...array_map('intval', explode('-', $gregorian)));
    if ($roundTrip !== [(int)$parts[1], (int)$parts[2], (int)$parts[3]]) return null;
    return $gregorian;
}

/** Format UTC timestamp/date values for human display without altering storage/API values. */
function jacquard_format_jalali_date(DateTimeInterface|string|null $value, bool $includeTime = false): string
{
    if ($value === null || $value === '') return '—';
    try {
        $date = $value instanceof DateTimeInterface
            ? DateTimeImmutable::createFromInterface($value)
            : new DateTimeImmutable($value, new DateTimeZone('UTC'));
        $date = $date->setTimezone(new DateTimeZone('UTC'));
        [$year, $month, $day] = jacquard_gregorian_to_jalali((int)$date->format('Y'), (int)$date->format('n'), (int)$date->format('j'));
        $formatted = sprintf('%04d/%02d/%02d', $year, $month, $day);
        if ($includeTime) $formatted .= ' ' . $date->format('H:i') . ' UTC';
        return jacquard_persian_digits($formatted);
    } catch (Throwable) {
        return '—';
    }
}
