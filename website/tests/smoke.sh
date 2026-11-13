#!/usr/bin/env bash
set -euo pipefail
find website -name '*.php' -print0 | xargs -0 -n1 php -l
mysql -h 127.0.0.1 -uroot -ptest-only-password -e 'CREATE DATABASE jacquard_migration CHARACTER SET utf8mb4'
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_migration < website/tests/schema-038.sql
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_migration -e "INSERT INTO installations VALUES ('3b01a77c-85a6-491e-94d8-28c4ec5e7138', REPEAT('a',64),REPEAT('A',64),'approved',UTC_TIMESTAMP(),UTC_TIMESTAMP(),'127.0.0.1','v','os','cpu',1,'pc'), ('f6bc3945-7683-4da5-b3c8-4d6e69d845f8',REPEAT('b',64),REPEAT('A',64),'blocked',UTC_TIMESTAMP(),UTC_TIMESTAMP(),'127.0.0.1','v','os','cpu',1,'pc'); INSERT INTO execution_events(installation_id,observed_at,ip) VALUES ('3b01a77c-85a6-491e-94d8-28c4ec5e7138',UTC_TIMESTAMP(),'127.0.0.1');"
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_migration < website/private/migrate-038-to-039.sql
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_migration < website/private/migrate-039-to-040.sql
[[ $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_migration -e 'SELECT status FROM devices') == blocked ]]
[[ $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_migration -e 'SELECT COUNT(*) FROM execution_events WHERE os_version=""') == 1 ]]
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test < website/private/schema.sql
config=$(mktemp)
export JACQUARD_TEST_RELEASES=$(mktemp -d)
export JACQUARD_TEST_KEY="$JACQUARD_TEST_RELEASES/license.pem"
openssl ecparam -name prime256v1 -genkey -noout -out "$JACQUARD_TEST_KEY"
openssl pkey -in "$JACQUARD_TEST_KEY" -pubout -out "$JACQUARD_TEST_RELEASES/public.pem"
trap 'if [[ -n "${server:-}" ]]; then kill "$server" 2>/dev/null || true; fi; rm -f "$config"; rm -rf "$JACQUARD_TEST_RELEASES"' EXIT
cat > "$config" <<'PHP'
<?php return ['db_dsn'=>'mysql:host=127.0.0.1;dbname=jacquard_test;charset=utf8mb4','db_user'=>'root','db_password'=>'test-only-password','admin_username'=>'test-admin','admin_hash'=>password_hash('test-only-admin-password', PASSWORD_DEFAULT),'download_dir'=>getenv('JACQUARD_TEST_RELEASES'),'base_path'=>'','allow_local_http'=>true,'license_private_key'=>getenv('JACQUARD_TEST_KEY')];
PHP
export JACQUARD_CONFIG="$config"
php -S 127.0.0.1:18999 -t website/public >/tmp/jacquard-php.log 2>&1 & server=$!
sleep 2
curl -fsS http://127.0.0.1:18999/ | grep -q 'تار و پود'
request='{"installationId":"3b01a77c-85a6-491e-94d8-28c4ec5e7138","secret":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","device":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","licenseProtocol":2,"appVersion":"test","osVersion":"Windows","cpuModel":"CPU","ramBytes":1024,"machineName":"PC","customerName":"First User","phone":"123","city":"Utrecht"}'
first=$(curl -fsS -H 'Content-Type: application/json' --data "$request" http://127.0.0.1:18999/api.php)
[[ "$first" == *'"status":"pending"'* ]]
[[ $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e 'SELECT customer_name FROM devices') == 'First User' ]]
export REQUEST_JSON="$request"
lookup_pending=$(python3 - <<'PYTEST'
import os,json
r=json.loads(os.environ['REQUEST_JSON']);r['checkOnly']=True;print(json.dumps(r))
PYTEST
)
pending_lookup=$(curl -fsS -H 'Content-Type: application/json' --data "$lookup_pending" http://127.0.0.1:18999/api.php)
[[ "$pending_lookup" == *'"status":"not_licensed"'* && $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e 'SELECT COUNT(*) FROM execution_events') == 1 ]]
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test -e "UPDATE devices SET status='blocked'"
second=$(curl -fsS -H 'Content-Type: application/json' --data "$request" http://127.0.0.1:18999/api.php)
[[ "$second" == *'"status":"blocked"'* ]]
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test -e "UPDATE devices SET status='approved',activation_code='arbitrary-unchecked-code'"
approved=$(curl -fsS -H 'Content-Type: application/json' --data "$request" http://127.0.0.1:18999/api.php)
export APPROVED_REPLY="$approved"
approved_token=$(python3 -c 'import json,os; print(json.loads(os.environ["APPROVED_REPLY"])["token"])')
python3 - <<'PYTEST'
import os,json,base64,pathlib,time
r=json.loads(os.environ['APPROVED_REPLY']); assert r['status']=='approved'
parts=r['token'].split('.'); assert len(parts)==3 and parts[0]=='JSL1'
def dec(s): return base64.urlsafe_b64decode(s+'='*(-len(s)%4))
payload=dec(parts[1]); signature=dec(parts[2]); c=json.loads(payload)
assert c['version']==1 and c['product']=='JacquardStudio' and c['device']=='A'*64 and c['licenseId']=='3b01a77c-85a6-491e-94d8-28c4ec5e7138'
assert c['expiresAt']-c['issuedAt']==604800 and c['notBefore']<=int(time.time())<c['expiresAt']
assert r['activationCode']=='arbitrary-unchecked-code' and c['activationHash']==__import__('hashlib').sha256(r['activationCode'].encode()).hexdigest().upper() and c['licenseExpiresAt'] is None
p=pathlib.Path(os.environ['JACQUARD_TEST_RELEASES']); (p/'payload').write_bytes(payload);(p/'signature').write_bytes(signature)
PYTEST
openssl dgst -sha256 -verify "$JACQUARD_TEST_RELEASES/public.pem" -signature "$JACQUARD_TEST_RELEASES/signature" "$JACQUARD_TEST_RELEASES/payload"
lookup_request=$(python3 - <<'PYTEST'
import os,json
r=json.loads(os.environ['REQUEST_JSON']);r.update(checkOnly=True,customerName='Lookup must not update',phone='999',city='Nowhere',osVersion='Do not store',cpuModel='Do not store',ramBytes=0,machineName='Do not store');print(json.dumps(r))
PYTEST
)
lookup=$(curl -fsS -H 'Content-Type: application/json' --data "$lookup_request" http://127.0.0.1:18999/api.php)
export LOOKUP_REPLY="$lookup"
python3 - <<'PYTEST'
import os,json,base64
r=json.loads(os.environ['LOOKUP_REPLY']);assert r['status']=='approved' and r['activationCode']=='arbitrary-unchecked-code'
p=r['token'].split('.')[1];c=json.loads(base64.urlsafe_b64decode(p+'='*(-len(p)%4)))
assert c['device']=='A'*64 and c['licenseId']=='3b01a77c-85a6-491e-94d8-28c4ec5e7138'
PYTEST
[[ $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e 'SELECT customer_name FROM devices') == 'First User' ]]
[[ $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e 'SELECT COUNT(*) FROM execution_events') == 3 ]]
unknown_lookup=$(python3 - <<'PYTEST'
import os,json
r=json.loads(os.environ['REQUEST_JSON']);r.update(installationId='f6bc3945-7683-4da5-b3c8-4d6e69d845f8',secret='b'*64,device='B'*64,checkOnly=True);print(json.dumps(r))
PYTEST
)
unknown_reply=$(curl -fsS -H 'Content-Type: application/json' --data "$unknown_lookup" http://127.0.0.1:18999/api.php)
[[ "$unknown_reply" == *'"status":"not_licensed"'* && $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e 'SELECT COUNT(*) FROM devices') == 1 ]]

wrong=${request/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb}
code=$(curl -sS -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' --data "$wrong" http://127.0.0.1:18999/api.php)
[[ "$code" == 403 ]]
count=$(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e 'SELECT COUNT(*) FROM execution_events')
[[ "$count" == 3 ]]
# The device expiry limits the signed lease, and reinstallation inherits device approval.
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test -e "UPDATE devices SET expires_at=UTC_TIMESTAMP()+INTERVAL 1 HOUR"
short=$(curl -fsS -H 'Content-Type: application/json' --data "$request" http://127.0.0.1:18999/api.php)
export SHORT_REPLY="$short"
python3 - <<'PYTEST'
import os,json,base64
r=json.loads(os.environ['SHORT_REPLY']); p=r['token'].split('.')[1]; c=json.loads(base64.urlsafe_b64decode(p+'='*(-len(p)%4)))
assert r['status']=='approved' and 0 < c['expiresAt']-c['issuedAt'] <= 3600 and c['licenseExpiresAt']==c['expiresAt']
PYTEST
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test -e "UPDATE devices SET expires_at=UTC_TIMESTAMP()-INTERVAL 1 SECOND"
expired=$(curl -fsS -H 'Content-Type: application/json' --data "$request" http://127.0.0.1:18999/api.php)
[[ "$expired" == *'"status":"expired"'* && "$expired" == *'"token":null'* ]]
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test -e "UPDATE devices SET expires_at=NULL"
reinstall=${request/3b01a77c-85a6-491e-94d8-28c4ec5e7138/f6bc3945-7683-4da5-b3c8-4d6e69d845f8}
reinstalled=$(curl -fsS -H 'Content-Type: application/json' --data "$reinstall" http://127.0.0.1:18999/api.php)
[[ "$reinstalled" == *'"status":"approved"'* ]]
# Old clients cannot consume a lease without checking the stored authorization.
legacy=${request/\"licenseProtocol\":2,/}
legacy_reply=$(curl -fsS -H 'Content-Type: application/json' --data "$legacy" http://127.0.0.1:18999/api.php)
[[ "$legacy_reply" == *'"status":"upgrade_required"'* && "$legacy_reply" == *'"token":null'* ]]

[[ $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e 'SELECT COUNT(*) FROM devices') == 1 ]]

cookies=$(mktemp); html=$(mktemp); fake=$(mktemp); downloaded=$(mktemp)
curl -fsS -c "$cookies" http://127.0.0.1:18999/admin.php > "$html"
csrf=$(sed -n 's/.*name="csrf" value="\([a-f0-9]*\)".*/\1/p' "$html" | head -1)
[[ ${#csrf} == 64 ]]
curl -fsS -b "$cookies" -c "$cookies" -X POST -d "csrf=$csrf&action=login&username=test-admin&password=test-only-admin-password" http://127.0.0.1:18999/admin.php -o /dev/null
curl -fsS -b "$cookies" http://127.0.0.1:18999/admin.php > "$html"
grep -q 'بارگذاری فایل نصب' "$html"
csrf=$(sed -n 's/.*name="csrf" value="\([a-f0-9]*\)".*/\1/p' "$html" | head -1)
device=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
# Site accepts an opaque code without checking its signature or device; date is mandatory.
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test -e "UPDATE devices SET status='pending',activation_code=NULL"
curl -fsS -b "$cookies" --data-urlencode "csrf=$csrf" --data-urlencode 'action=save_device' --data-urlencode "device=$device" --data-urlencode 'status=approved' --data-urlencode 'activation_code=not-a-signed-code' http://127.0.0.1:18999/admin.php > "$html"
grep -q 'تاریخ پایان اعتبار' "$html"
curl -fsS -b "$cookies" --data-urlencode "csrf=$csrf" --data-urlencode 'action=save_device' --data-urlencode "device=$device" --data-urlencode 'status=pending' --data-urlencode 'activation_code=not-a-signed-code' --data-urlencode 'expiry=2090-01-01' --data-urlencode 'customer_name=Test User' --data-urlencode 'phone=12345' --data-urlencode 'city=Amsterdam' --data-urlencode 'notes=<script>' http://127.0.0.1:18999/admin.php > "$html"
grep -q 'Test User' "$html"; grep -q 'Amsterdam' "$html"
curl -fsS -b "$cookies" "http://127.0.0.1:18999/admin.php?device=$device" > "$html"
grep -q 'تاریخچه اتصال' "$html";grep -q 'Windows' "$html";grep -q '&lt;script&gt;' "$html";grep -q '2090-01-01' "$html"
online=$(curl -fsS -H 'Content-Type: application/json' --data "$request" http://127.0.0.1:18999/api.php)
export ONLINE_REPLY="$online";python3 - <<'PYTEST'
import json,os
import hashlib,base64
r=json.loads(os.environ['ONLINE_REPLY']); assert r['status']=='approved' and r['activationCode']=='not-a-signed-code'
p=r['token'].split('.')[1]; c=json.loads(base64.urlsafe_b64decode(p+'='*(-len(p)%4)))
assert c['activationHash']==hashlib.sha256(r['activationCode'].encode()).hexdigest().upper() and c['licenseExpiresAt']>c['expiresAt']

PYTEST

[[ $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e "SELECT expires_at FROM devices") == '2090-01-02 00:00:00' ]]
code=$(curl -sS -o /dev/null -w '%{http_code}' --data "csrf=$csrf&action=delete_device&device=$device" http://127.0.0.1:18999/admin.php)
[[ "$code" == 403 ]]
{ printf MZ; head -c 2048 /dev/zero; } > "$fake"
curl -fsS -b "$cookies" -F "csrf=$csrf" -F 'action=upload' -F "installer=@$fake;filename=setup.exe" http://127.0.0.1:18999/admin.php > "$html"
grep -q 'فایل منتشر شد' "$html"
curl -fsS http://127.0.0.1:18999/download.php > "$downloaded"
cmp "$fake" "$downloaded"
printf 'not an installer' > "$fake";head -c 2048 /dev/zero >> "$fake"
curl -fsS -b "$cookies" -F "csrf=$csrf" -F 'action=upload' -F "installer=@$fake;filename=evil.php" http://127.0.0.1:18999/admin.php > "$html"
grep -q 'اجرایی ویندوز نیست' "$html"
curl -fsS http://127.0.0.1:18999/download.php > "$fake";cmp "$fake" "$downloaded"
curl -fsS -b "$cookies" "http://127.0.0.1:18999/admin.php?device=$device&delete=1" > "$html";grep -q 'تأیید می‌کنید' "$html"
curl -fsS -b "$cookies" -X POST -d "csrf=$csrf&action=delete_device&device=$device" http://127.0.0.1:18999/admin.php > "$html"
[[ $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e 'SELECT COUNT(*) FROM execution_events') == 0 ]]
new=$(curl -fsS -H 'Content-Type: application/json' --data "$request" http://127.0.0.1:18999/api.php)
[[ "$new" == *'"status":"pending"'* ]]
# The manual code is stored but cannot approve a pending record without an administrator.
export REQUEST_JSON="$request" MANUAL_TOKEN="$approved_token"
manual_request=$(python3 - <<'PYTEST'
import os,json
r=json.loads(os.environ['REQUEST_JSON']);r['manualToken']=os.environ['MANUAL_TOKEN'];print(json.dumps(r))
PYTEST
)
manual_reply=$(curl -fsS -H 'Content-Type: application/json' --data "$manual_request" http://127.0.0.1:18999/api.php)
[[ "$manual_reply" == *'"status":"pending"'* ]]
[[ $(mysql -h 127.0.0.1 -uroot -ptest-only-password -N -B jacquard_test -e 'SELECT status FROM devices') == pending ]]
mysql -h 127.0.0.1 -uroot -ptest-only-password jacquard_test -e "UPDATE devices SET status='blocked'"
manual_reply=$(curl -fsS -H 'Content-Type: application/json' --data "$manual_request" http://127.0.0.1:18999/api.php)
[[ "$manual_reply" == *'"status":"blocked"'* ]]

rm -f "$cookies" "$html" "$fake" "$downloaded"
echo 'Website API smoke tests passed.'
