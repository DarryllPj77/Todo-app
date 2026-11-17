-- Run with psql in your own interactive terminal as a PostgreSQL administrator.
-- Stops on errors; never drops/reuses an existing role or database.
\set ON_ERROR_STOP on
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'todoapp_sync_dev') THEN
        RAISE EXCEPTION 'Role todoapp_sync_dev already exists. Review it; no existing role will be changed.';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_database WHERE datname = 'todoapp_phone_sync') THEN
        RAISE EXCEPTION 'Database todoapp_phone_sync already exists. Review it; no existing database will be changed.';
    END IF;
END
$$;

CREATE ROLE todoapp_sync_dev NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
-- Session setting only; psql hashes the new password before sending it to the server.
SET password_encryption = 'scram-sha-256';
\password todoapp_sync_dev
CREATE DATABASE todoapp_phone_sync OWNER todoapp_sync_dev;
REVOKE ALL ON DATABASE todoapp_phone_sync FROM PUBLIC;
ALTER ROLE todoapp_sync_dev LOGIN;
\echo 'Created todoapp_phone_sync and its dedicated non-superuser role. Configure API credentials locally next.'
