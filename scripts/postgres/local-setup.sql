-- Preparação do PostgreSQL local para a contabilidade. Correr uma vez como superutilizador:
--   psql -U postgres -h localhost -f scripts/postgres/local-setup.sql
--
-- O servidor é o mesmo do NexGen ERP, mas a base de dados é própria: o ERP nunca acede
-- às tabelas da contabilidade (integração só por eventos/REST).
CREATE ROLE accounting LOGIN PASSWORD 'accounting' NOSUPERUSER;
CREATE DATABASE accounting OWNER accounting ENCODING 'UTF8' TEMPLATE template0;
CREATE DATABASE accounting_test OWNER accounting ENCODING 'UTF8' TEMPLATE template0;
\connect accounting
CREATE EXTENSION IF NOT EXISTS citext;
\connect accounting_test
CREATE EXTENSION IF NOT EXISTS citext;
