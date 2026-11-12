CREATE TABLE IF NOT EXISTS devices (
 device_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 status ENUM('pending','approved','blocked') NOT NULL DEFAULT 'pending',
 expires_at DATETIME NULL,
 customer_name VARCHAR(120) NOT NULL DEFAULT '', phone VARCHAR(60) NOT NULL DEFAULT '', city VARCHAR(100) NOT NULL DEFAULT '', notes VARCHAR(500) NOT NULL DEFAULT '',
 created_at DATETIME NOT NULL,
 INDEX(status), INDEX(expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS installations (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 secret_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 device_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 status ENUM('pending','approved','blocked') NOT NULL DEFAULT 'pending',
 created_at DATETIME NOT NULL, last_seen DATETIME NOT NULL,
 last_ip VARCHAR(45) NOT NULL, app_version VARCHAR(50) NOT NULL,
 os_version VARCHAR(150) NOT NULL, cpu_model VARCHAR(160) NOT NULL,
 ram_bytes BIGINT UNSIGNED NOT NULL DEFAULT 0, machine_name VARCHAR(100) NOT NULL,
 INDEX(last_seen), INDEX(status), INDEX(device_hash),
 FOREIGN KEY (device_hash) REFERENCES devices(device_hash) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS execution_events (
 id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
 installation_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 observed_at DATETIME NOT NULL, ip VARCHAR(45) NOT NULL,
 os_version VARCHAR(150) NOT NULL DEFAULT '', app_version VARCHAR(50) NOT NULL DEFAULT '', cpu_model VARCHAR(160) NOT NULL DEFAULT '', ram_bytes BIGINT UNSIGNED NOT NULL DEFAULT 0, machine_name VARCHAR(100) NOT NULL DEFAULT '',
 FOREIGN KEY (installation_id) REFERENCES installations(id) ON DELETE CASCADE,
 INDEX(observed_at), INDEX(installation_id,observed_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS request_limits (
 scope VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 window_start DATETIME NOT NULL, hits INT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
