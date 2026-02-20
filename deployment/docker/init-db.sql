-- Create extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Set timezone
SET timezone = 'UTC';

-- Create additional indexes for better performance (already in migrations, but good to have)
-- These will be created by EF Core migrations automatically

-- You can add initial data here if needed
-- INSERT INTO "Users" ("Id", "DeviceId", "Nickname", "CreatedAt")
-- VALUES (uuid_generate_v4(), 'system', 'System', NOW());
