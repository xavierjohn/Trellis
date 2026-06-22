# Trellis.Persistence.Abstractions

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Persistence.Abstractions.svg)](https://www.nuget.org/packages/Trellis.Persistence.Abstractions)

Store-agnostic persistence contracts for Trellis.

This package hosts the seams that let Trellis persist without committing to a specific store:

- `IUnitOfWork` — the commit boundary the standard command pipeline drives.
- `IInboxStore` + `InboxRecord` — the idempotent-consumer dedup record store SPI.
- `IConsumerCheckpointStore` — a pull consumer's durable resume cursor.

It depends only on `Trellis.Core`, so an adapter can implement these over EF Core (the shipped default), Dapper, ADO, Cosmos DB, or any other store without taking a dependency on a specific persistence technology.
