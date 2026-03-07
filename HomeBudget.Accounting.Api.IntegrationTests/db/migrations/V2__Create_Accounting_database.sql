-- evolve-tx-off
IF NOT EXISTS(SELECT * FROM sys.databases WITH (NOLOCK) WHERE name = N'Accounting')
BEGIN
    CREATE DATABASE [HomeBudget.Accounting];
END

GO