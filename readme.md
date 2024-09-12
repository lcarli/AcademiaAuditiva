## Inicializar o container do SQL

``` sh
docker compose up -d
```

## Autorizar o novo user recem registrado

Copia o Id do banco:

``` sql
SELECT * FROM [AcademiaAuditiva].[dbo].[AspNetUsers]
```

Atualiza as confirmações:

``` sql
UPDATE [AcademiaAuditiva].[dbo].[AspNetUsers]
SET PhoneNumberConfirmed = 1, EmailConfirmed = 1
WHERE Id = ''
```