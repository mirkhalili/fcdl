<?php
declare(strict_types=1);
// Codes are signed by the same P-256 private key used by the issuer. Never trust raw claims before verification.
function verifiedCode(array $config,string $token,string $device,bool $allowExpired=false): ?array {
 if(strlen($token)>4096 || !preg_match('/^JSL1\.([A-Za-z0-9_-]+)\.([A-Za-z0-9_-]+)$/D',$token,$m))return null;
 $decode=static function(string $s): string|false {return base64_decode(strtr($s,'-_','+/').str_repeat('=',(4-strlen($s)%4)%4),true);};
 $payload=$decode($m[1]);$sig=$decode($m[2]);if($payload===false || strlen($payload)>2048 || $sig===false || strlen($sig)>80)return null;
 $path=$config['license_private_key'] ?? '';if(!is_string($path) || !is_file($path) || !is_readable($path))return null;
 $pem=file_get_contents($path);if($pem===false || strlen($pem)>8192)return null;$private=openssl_pkey_get_private($pem);if($private===false)return null;
 $key=openssl_pkey_get_details($private);if(($key['type'] ?? null)!==OPENSSL_KEYTYPE_EC || ($key['ec']['curve_name'] ?? '')!=='prime256v1' || !isset($key['key']))return null;
 if(openssl_verify($payload,$sig,$key['key'],OPENSSL_ALGO_SHA256)!==1)return null;
 $c=json_decode($payload,true,16);if(!is_array($c) || ($c['version'] ?? null)!==1 || ($c['product'] ?? null)!=='JacquardStudio' || !is_string($c['device'] ?? null) || !hash_equals($device,$c['device']) || !is_string($c['licenseId'] ?? null) || !preg_match('/^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$/D',$c['licenseId']) || $c['licenseId']==='00000000-0000-0000-0000-000000000000')return null;
 foreach(['issuedAt','notBefore','expiresAt'] as $f)if(!is_int($c[$f] ?? null))return null;
 $now=time();if($c['issuedAt']<=0 || $c['notBefore']<$c['issuedAt']-300 || $c['expiresAt']<=$c['issuedAt'] || $c['expiresAt']-$c['issuedAt']>604800 || $c['notBefore'] >= $c['expiresAt'] || $now<$c['notBefore'] || (!$allowExpired && $now >= $c['expiresAt']))return null;
 if(isset($c['customerName']) && (!is_string($c['customerName']) || strlen($c['customerName'])>480 || preg_match('/[\x00-\x1f\x7f]/',$c['customerName'])))return null;
 return $c;
}
