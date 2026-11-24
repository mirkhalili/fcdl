<?php
declare(strict_types=1);
require __DIR__.'/../public/_lib/jalali.php';

function check_calendar(bool $condition, string $message): void
{
    if (!$condition) throw new RuntimeException('Jalali calendar test failed: '.$message);
}

check_calendar(jacquard_gregorian_to_jalali(2023,3,21)===[1402,1,1], 'Gregorian Nowruz 2023');
check_calendar(jacquard_gregorian_to_jalali(2024,3,20)===[1403,1,1], 'Gregorian Nowruz 2024');
check_calendar(jacquard_gregorian_to_jalali(2025,3,21)===[1404,1,1], 'Gregorian Nowruz 2025');
check_calendar(jacquard_gregorian_to_jalali(2100,12,30)===[1479,10,10], '2100 Gregorian century leap rule');
check_calendar(jacquard_jalali_to_gregorian(1402,1,1)==='2023-03-21', 'Jalali to Gregorian conversion');
check_calendar(jacquard_jalali_to_gregorian(1399,12,30)==='2021-03-20', 'valid Jalali leap day');
check_calendar(jacquard_jalali_to_gregorian(1468,10,12)==='2090-01-01', 'future date conversion');
check_calendar(jacquard_jalali_to_gregorian(1479,10,10)==='2100-12-30', 'last supported Gregorian day');
check_calendar(jacquard_parse_jalali_date('۱۴۰۲/۰۱/۰۱')==='2023-03-21', 'Persian numerals');
check_calendar(jacquard_parse_jalali_date('١٤٠٢-٠١-٠١')==='2023-03-21', 'Arabic numerals and hyphens');
check_calendar(jacquard_parse_jalali_date('۱۴۰۲/۱۲/۳۰')===null, 'reject invalid leap day');
check_calendar(jacquard_parse_jalali_date('۱۴۰۰/۱۲/۳۰')===null, 'reject non-leap year Esfand 30');
check_calendar(jacquard_parse_jalali_date('۱۳۷۸/۱۰/۱۱')==='2000-01-01', 'first supported business date');
check_calendar(jacquard_parse_jalali_date('۱۳۷۸/۱۰/۱۰')===null, 'reject date before supported business range');
check_calendar(jacquard_parse_jalali_date('۱۴۷۹/۱۰/۱۰')==='2100-12-30', 'last supported business date');
check_calendar(jacquard_parse_jalali_date('۱۴۷۹/۱۰/۱۱')===null, 'reject date after supported business range');
check_calendar(jacquard_format_jalali_date('2023-03-21 12:34:00')==='۱۴۰۲/۰۱/۰۱', 'date display is Jalali with Persian digits');
check_calendar(jacquard_format_jalali_date('2023-03-21 12:34:00',true)==='۱۴۰۲/۰۱/۰۱ ۱۲:۳۴ UTC', 'timestamp display keeps UTC time');
check_calendar(jacquard_format_jalali_date('not-a-date')==='—', 'invalid manifest date is not echoed in Gregorian form');
echo "Jalali calendar tests passed.\n";
