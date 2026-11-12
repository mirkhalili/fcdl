#!/usr/bin/env bash
set -euo pipefail
find website -name '*.php' -print0 | xargs -0 -n1 php -l
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test < website/private/schema.sql
config=$(mktemp)
trap 'kill "${server:-0}" 2>/dev/null || true; rm -f "$config"' EXIT
cat > "$config" <<'PHP'
<?php return ['db_dsn'=>'mysql:host=127.0.0.1;dbname=jacquard_test;charset=utf8mb4','db_user'=>'root','db_password'=>'test-only-password','admin_hash'=>'$2y$10$g06pBH8zVR4bmVcJPAIhTOu2FlRfWoDgk1y9O7P19OgCAQzXRE9Ii','base_path'=>'','allow_local_http'=>true];
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
echo 'Website API smoke tests passed.'
