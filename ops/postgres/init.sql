-- Each service owns a separate logical database. Service migrators own schemas.
SELECT 'CREATE DATABASE alpha_identity OWNER alpha'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'alpha_identity')\gexec

SELECT 'CREATE DATABASE alpha_tenant OWNER alpha'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'alpha_tenant')\gexec

SELECT 'CREATE DATABASE alpha_asset OWNER alpha'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'alpha_asset')\gexec

SELECT 'CREATE DATABASE alpha_tag_catalog OWNER alpha'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'alpha_tag_catalog')\gexec

SELECT 'CREATE DATABASE alpha_edge OWNER alpha'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'alpha_edge')\gexec

SELECT 'CREATE DATABASE alpha_gateway OWNER alpha'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'alpha_gateway')\gexec

SELECT 'CREATE DATABASE alpha_telemetry OWNER alpha'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'alpha_telemetry')\gexec

SELECT 'CREATE DATABASE alpha_alarm OWNER alpha'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'alpha_alarm')\gexec

SELECT 'CREATE DATABASE alpha_reporting OWNER alpha'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'alpha_reporting')\gexec
