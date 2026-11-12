#!/usr/bin/env bash
set -euo pipefail
find website -name '*.php' -print0 | xargs -0 -n1 php -l
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test < website/private/schema.sql
config=$(mktemp)
export JACQUARD_TEST_RELEASES=$(mktemp -d)
trap 'if [[ -n "${server:-}" ]]; then kill "$server" 2>/dev/null || true; fi; rm -f "$config"; rm -rf "$JACQUARD_TEST_RELEASES"' EXIT
cat > "$config" <<'PHP'
<?php return ['db_dsn'=>'mysql:host=127.0.0.1;dbname=jacquard_test;charset=utf8mb4','db_user'=>'root','db_password'=>'test-only-password','admin_username'=>'test-admin','admin_hash'=>password_hash('test-only-admin-password', PASSWORD_DEFAULT),'download_dir'=>getenv('JACQUARD_TEST_RELEASES'),'base_path'=>'','allow_local_http'=>true];
PHP
export JACQUARD_CONFIG="$config"
php -S 127.0.0.1:18999 -t website/public >/tmp/jacquard-php.log 2>&1 & server=$!
sleep 2
curl -fsS http://127.0.0.1:18999/ | grep -q 'تار و پود'
request='{"installationId":"3b01a77c-85a6-491e-94d8-28c4ec5e7138","secret":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","device":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","appVersion":"test","osVersion":"Windows","cpuModel":"CPU","ramBytes":1024,"machineName":"PC"}'
first=$(curl -fsS -H 'Content-Type: application/json' --data "$request" http://127.0.0.1:18999/api.php)
[[ "$first" == *'"status":"pending"'* ]]
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test -e "UPDATE installations SET status='blocked'"
second=$(curl -fsS -H 'Content-Type: application/json' --data "$request" http://127.0.0.1:18999/api.php)
[[ "$second" == *'"status":"blocked"'* ]]
wrong=${request/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb}
code=$(curl -sS -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' --data "$wrong" http://127.0.0.1:18999/api.php)
[[ "$code" == 403 ]]
count=$(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e 'SELECT COUNT(*) FROM execution_events')
[[ "$count" == 2 ]]
cookies=$(mktemp); html=$(mktemp); fake=$(mktemp); downloaded=$(mktemp)
curl -fsS -c "$cookies" http://127.0.0.1:18999/admin.php > "$html"
csrf=$(sed -n 's/.*name="csrf" value="\([a-f0-9]*\)".*/\1/p' "$html" | head -1)
[[ ${#csrf} == 64 ]]
curl -fsS -b "$cookies" -c "$cookies" -X POST -d "csrf=$csrf&action=login&username=test-admin&password=test-only-admin-password" http://127.0.0.1:18999/admin.php -o /dev/null
curl -fsS -b "$cookies" http://127.0.0.1:18999/admin.php > "$html"
grep -q 'بارگذاری فایل نصب' "$html"
csrf=$(sed -n 's/.*name="csrf" value="\([a-f0-9]*\)".*/\1/p' "$html" | head -1)
{ printf MZ; head -c 2048 /dev/zero; } > "$fake"
curl -fsS -b "$cookies" -F "csrf=$csrf" -F 'action=upload' -F "installer=@$fake;filename=setup.exe" http://127.0.0.1:18999/admin.php > "$html"
grep -q 'فایل منتشر شد' "$html"
curl -fsS http://127.0.0.1:18999/download.php > "$downloaded"
cmp "$fake" "$downloaded"
printf 'not an installer' > "$fake";head -c 2048 /dev/zero >> "$fake"
curl -fsS -b "$cookies" -F "csrf=$csrf" -F 'action=upload' -F "installer=@$fake;filename=evil.php" http://127.0.0.1:18999/admin.php > "$html"
grep -q 'اجرایی ویندوز نیست' "$html"
curl -fsS http://127.0.0.1:18999/download.php > "$fake";cmp "$fake" "$downloaded"
rm -f "$cookies" "$html" "$fake" "$downloaded"
echo 'Website API smoke tests passed.'
