/* =====================================================================
   Felibow - Banco de dados da loja
   Arquivo 06 (opcional, recomendado): usuario da APLICACAO com privilegio minimo.

   A API/loja NAO deve se conectar como "sa". Este script cria o login
   felibow_app com permissao apenas de leitura, escrita e execucao de
   procedures no banco FelibowLoja (sem DDL).

   A senha NAO fica no arquivo: informe-a com a variavel do sqlcmd:
     sqlcmd -S localhost,1433 -U sa -P "<senha-sa>" -C -f 65001 -b ^
            -v APP_PASSWORD="<senha-forte-da-aplicacao>" -i 06_usuario_aplicacao.sql
   ===================================================================== */

USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'felibow_app')
    CREATE LOGIN felibow_app WITH PASSWORD = N'$(APP_PASSWORD)', CHECK_POLICY = ON;
GO

USE FelibowLoja;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'felibow_app')
    CREATE USER felibow_app FOR LOGIN felibow_app;
GO

ALTER ROLE db_datareader ADD MEMBER felibow_app;
ALTER ROLE db_datawriter ADD MEMBER felibow_app;
GRANT EXECUTE ON SCHEMA::dbo TO felibow_app;
GO
