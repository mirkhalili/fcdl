-- Backup first; execute exactly once on a fully migrated 039 database.
ALTER TABLE devices ADD COLUMN activation_code TEXT NULL AFTER expires_at;
