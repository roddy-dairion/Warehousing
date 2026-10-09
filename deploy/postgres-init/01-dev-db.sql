-- Development only. Never use these credentials outside a developer machine.
CREATE ROLE inventory LOGIN PASSWORD 'inventory_dev_pw' CREATEDB;
CREATE DATABASE inventory_dev OWNER inventory;
