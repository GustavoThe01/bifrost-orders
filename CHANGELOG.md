# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não lançado]

### Adicionado
- Solution `Bifrost.slnx` com projetos Domain, Application, Infrastructure e testes.
- `Directory.Build.props` (Nullable, warnings como erro), Central Package Management e `.editorconfig`.
- Teste de arquitetura garantindo que Domain não depende das outras camadas.
- Pipeline de CI no GitHub Actions.
- `Bifrost.Api` com endpoint `/health` (health checks nativos).
- ADR 0001 — Clean Architecture; ADR 0002 — dispatcher próprio em vez de MediatR.
- Base do domínio: `AggregateRoot` com eventos de domínio e `DomainException`.
- Value objects `Money`, `Email` e `Address` (snapshot do endereço de entrega).
- Agregado `Order` com `OrderItem`, máquina de estados (Pending → Confirmed → Paid → Shipped → Delivered / Cancelled) e eventos para cada transição.
- 32 testes unitários do domínio.
- Agregado `Product` no catálogo, com SKU único.
- Persistência com EF Core e SQLite: `BifrostDbContext`, `IEntityTypeConfiguration`, owned type para `Address` e complex type para `Money`.
- Dinheiro gravado em centavos (`INTEGER`) e status do pedido gravado como texto.
- Migration `InitialCreate` e seed do catálogo, aplicados ao subir a API em desenvolvimento.
- Projeto `Bifrost.IntegrationTests` e 15 testes novos: 9 de persistência e 6 de `Product`.
- CQRS com dispatcher próprio: `ICommand`, `IQuery`, handlers e `IDispatcher`.
- Pipeline com decorators: validação com FluentValidation nos commands e log com duração nos commands e queries.
- Commands para criar, confirmar e cancelar pedidos; queries para detalhar pedido e listar produtos.
- Repositórios e leituras com EF Core em `Bifrost.Infrastructure`.
- Projeto `Bifrost.Application.Tests` e 15 testes novos: 13 de pipeline e 2 de fluxo completo.
