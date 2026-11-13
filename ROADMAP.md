# Roadmap — Bifrost (20 dias / 4 semanas)

## Semana 1 — Fundação  → `v0.1.0`

### Dia 01 — Solution
- [x] Projetos Domain / Application / Infrastructure / Api + projetos de teste
- [x] `Directory.Build.props`, **Central Package Management** (`Directory.Packages.props`), `.editorconfig`
- [x] GitHub Actions (build + test)
- [x] ADR 0001 — Clean Architecture · ADR 0002 — Dispatcher próprio em vez de MediatR (licença e aprendizado)

### Dia 02 — Domínio
- [x] Agregado `Order` com `OrderItem`, value objects (`Money`, `Address`, `Email`)
- [x] Máquina de estados do pedido e domain events
- [x] Testes unitários (TDD)

### Dia 03 — EF Core + SQLite
- [x] `DbContext`, `IEntityTypeConfiguration<T>`, owned types
- [x] Primeira migration e seed de produtos

### Dia 04 — CQRS
- [x] `ICommand`/`IQuery`, handlers e dispatcher
- [x] Pipeline behaviors (decorators): validação com FluentValidation e logging

### Dia 05 — API
- [x] Controllers de produtos e pedidos, DTOs
- [x] `IExceptionHandler` global + ProblemDetails, OpenAPI (Scalar ou Swagger UI)
- [x] Tag `v0.1.0`

## Semana 2 — Consultas e testes  → `v0.2.0`

### Dia 06 — Lado de leitura
- [x] Queries com `AsNoTracking`, projeções e paginação
- [x] Leituras críticas com **Dapper** (CQRS de verdade) — registrar em ADR 0003

### Dia 07 — Result pattern
- [x] `Result<T>` / `Error` e mapeamento para HTTP (400, 404, 409, 422)

### Dia 08 — Testes
- [x] Unitários dos handlers (NSubstitute)
- [x] Integração com `WebApplicationFactory` + SQLite em memória

### Dia 09 — Concorrência
- [x] Token de concorrência no estoque; reservar estoque ao confirmar pedido
- [x] Teste de pedidos simultâneos disputando o último item

### Dia 10 — Logging
- [x] Serilog estruturado, middleware de **correlation id**
- [x] Tag `v0.2.0`

## Semana 3 — Segurança e integrações  → `v0.3.0`

### Dia 11 — Autenticação
- [x] JWT (emissão e validação), roles `Admin` e `Customer`

### Dia 12 — Autorização
- [x] Políticas: cliente só acessa os próprios pedidos; testes 401/403

### Dia 13 — Outbox pattern
- [ ] Eventos salvos na mesma transação do pedido
- [ ] `BackgroundService` publica e marca como processado

### Dia 14 — Integração externa
- [ ] Gateway de pagamento simulado via `HttpClientFactory`
- [ ] Retry, timeout e circuit breaker (Microsoft.Extensions.Http.Resilience)

### Dia 15 — Docker
- [ ] Dockerfile multi-stage, docker-compose, health checks (`/health/ready`, `/health/live`)
- [ ] Preencher "Como rodar"; tag `v0.3.0`

## Semana 4 — Produção  → `v1.0.0`

### Dia 16 — Performance
- [ ] HybridCache / Output Cache no catálogo, rate limiting nativo

### Dia 17 — Versionamento
- [ ] `Asp.Versioning` (v1/v2 de um endpoint) e documentação por versão

### Dia 18 — Observabilidade
- [ ] OpenTelemetry (traces + métricas); dashboard do .NET Aspire opcional

### Dia 19 — Qualidade
- [ ] NetArchTest (regras de dependência entre camadas), cobertura, analyzers
- [ ] Teste de carga simples com k6 no endpoint de criação de pedido

### Dia 20 — Entrega
- [ ] README final com diagrama C4 (Mermaid), fluxo de um pedido ponta a ponta
- [ ] `CHANGELOG.md`, tag `v1.0.0`, deploy opcional (Azure App Service / Fly.io)

## Ideias de evolução (opcional)
- Mensageria real (RabbitMQ) no lugar do outbox local
- Frontend simples (Blazor) consumindo a API
