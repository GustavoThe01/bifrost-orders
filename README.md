# Bifrost · orders-api

> *Bifrost, a ponte arco-íris entre os reinos, leva cada pedido do carrinho até a entrega.*

API de **pedidos de e-commerce** com **ASP.NET Core (.NET 10)**, **Clean Architecture**, **CQRS**
e **EF Core + SQLite**. Reaproveita o modelo de dados do [Mercurium](../mercurium-sql/).

## Competências demonstradas
- **Clean Architecture** (Domain, Application, Infrastructure, Api) e **CQRS**
  (escrita com EF Core, leitura com Dapper)
- Dispatcher de comandos/queries próprio com **pipeline behaviors** (validação, logging)
- **EF Core:** Fluent API, owned types para value objects, migrations, concorrência otimista
- **FluentValidation**, **Result pattern**, erros mapeados para ProblemDetails
- Autenticação **JWT** e autorização por **políticas**
- **Outbox pattern** + `BackgroundService` para eventos de domínio confiáveis
- Resiliência com `HttpClientFactory` + **Microsoft.Extensions.Http.Resilience** (retry, circuit breaker)
- Cache (HybridCache / Output Cache), **rate limiting** nativo, versionamento de API
- Observabilidade com **Serilog** e **OpenTelemetry**
- Testes: xUnit, NSubstitute, `WebApplicationFactory`, NetArchTest
- Docker, docker-compose, health checks, GitHub Actions

## Domínio
`Customer` · `Product` · `Order` (Pending → Paid → Shipped → Delivered / Cancelled) · `OrderItem` · `Payment`

## Arquitetura planejada
```
Bifrost.slnx
├── src/
│   ├── Bifrost.Domain/          # entidades, value objects, eventos, regras
│   ├── Bifrost.Application/     # commands, queries, handlers, validações
│   ├── Bifrost.Infrastructure/  # EF Core, Dapper, outbox, clientes HTTP
│   └── Bifrost.Api/             # controllers, middlewares, DI, auth
└── tests/
    ├── Bifrost.Domain.Tests/
    ├── Bifrost.Application.Tests/
    ├── Bifrost.IntegrationTests/
    └── Bifrost.ArchitectureTests/
```

## Como rodar
_A preencher no Dia 15 (Docker)._

## Status
Veja o [ROADMAP.md](ROADMAP.md).
