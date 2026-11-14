-- Run exactly once on a backup-tested 038 database, before uploading the new PHP files.
-- MySQL DDL is not transactional: if interrupted, restore the backup rather than rerun blindly.
CREATE TABLE devices (
 device_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 status ENUM('pending','approved','blocked') NOT NULL DEFAULT 'pending', expires_at DATETIME NULL,
 customer_name VARCHAR(120) NOT NULL DEFAULT '', phone VARCHAR(60) NOT NULL DEFAULT '', city VARCHAR(100) NOT NULL DEFAULT '', notes VARCHAR(500) NOT NULL DEFAULT '',
 created_at DATETIME NOT NULL, INDEX(status), INDEX(expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
-- Multiple historical installations: blocked takes precedence over approved for safety.
INSERT INTO devices(device_hash,status,created_at)
 SELECT device_hash, CASE WHEN SUM(status='blocked')>0 THEN 'blocked' WHEN SUM(status='approved')>0 THEN 'approved' ELSE 'pending' END, MIN(created_at)
 FROM installations GROUP BY device_hash;
ALTER TABLE installations ADD INDEX(device_hash), ADD CONSTRAINT fk_installation_device FOREIGN KEY(device_hash) REFERENCES devices(device_hash) ON DELETE CASCADE;
-- Old events have no historical OS snapshot; leave new fields empty rather than inventing it.
ALTER TABLE execution_events ADD COLUMN os_version VARCHAR(150) NOT NULL DEFAULT '', ADD COLUMN app_version VARCHAR(50) NOT NULL DEFAULT '', ADD COLUMN cpu_model VARCHAR(160) NOT NULL DEFAULT '', ADD COLUMN ram_bytes BIGINT UNSIGNED NOT NULL DEFAULT 0, ADD COLUMN machine_name VARCHAR(100) NOT NULL DEFAULT '';
