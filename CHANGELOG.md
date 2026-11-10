# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não lançado]

### Adicionado
- Leituras de pedido com Dapper: detalhe com `QueryMultiple` (cabeçalho e itens numa ida ao banco) e listagem com o total somado em SQL.
- `GET /api/v1/orders`: listagem paginada, do pedido mais recente para o mais antigo, com filtro por cliente e status.
- Catálogo lido com EF Core sem rastreamento e com projeção direta para DTO.
- Validação também no pipeline de queries (`ValidationQueryDecorator`).
- ADR 0003 — leituras críticas com Dapper, escrita com EF Core.
- 11 testes novos.
- Result pattern: commands e queries devolvem `Result<T>`, com `Error` de código estável e categoria (`Validation`, `NotFound`, `Conflict`, `BusinessRule`).
- Mapeamento único de categoria para HTTP (400, 404, 409 e 422) em `ResultExtensions`, sempre em ProblemDetails e com o campo `code`.
- `BusinessRuleCommandDecorator`, que converte a `DomainException` do domínio em `Result` na fronteira da aplicação.
- Os decorators de validação devolvem falha sem lançar exceção, usando um membro estático abstrato (`TResult.Failure`).
- O `GlobalExceptionHandler` passa a tratar só o inesperado (500).
- ADR 0004 — Result pattern na fronteira da aplicação.
- 3 testes novos.
- Testes unitários de cada handler isolado (sem dispatcher), incluindo a junção de itens repetidos com o preço do catálogo e o decorator que converte `DomainException`.
- Cenários de API: filtro da listagem por cliente e status, cancelamento sem motivo, rota com id inválido, respostas de erro no OpenAPI e erro inesperado devolvendo 500 sem detalhe interno.
- 14 testes novos.
- Reserva de estoque ao confirmar o pedido e devolução ao cancelar um pedido confirmado, na mesma transação do pedido.
- Token de concorrência `version` no produto (migration `AddProductVersion`), e `ConcurrencyConflictException` traduzida da exceção do EF Core.
- Conflito de concorrência vira `Result` com 409 (`concurrency_conflict`) pelo decorator do pipeline.
- Teste com 10 pedidos disputando a última unidade, num SQLite em arquivo: um confirmado, estoque zerado e nunca negativo.
- 9 testes novos.

### Removido
- `NotFoundException` e `ConflictException`: essas falhas agora voltam como `Result`.

## [0.1.0] - 2026-11-06

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
- API REST: `ProductsController` e `OrdersController`, com criação, detalhe, confirmação e cancelamento de pedidos.
- `CreateProductCommand`, com SKU único e resposta 409 quando repetido.
- `GlobalExceptionHandler`: erros em ProblemDetails (RFC 9457), com `traceId` e validação campo a campo.
- Documentação da API com OpenAPI e Scalar, disponível só em desenvolvimento.
- 10 testes novos, com a API inteira rodando pelo `WebApplicationFactory`.
