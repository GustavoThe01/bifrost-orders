# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não lançado]

### Adicionado
- Outbox pattern: tabela `outbox_messages` (migration `AddOutbox`), com índice parcial das mensagens pendentes.
- `OutboxInterceptor` do EF Core: os eventos de domínio viram mensagens no mesmo `SaveChanges`, e portanto na mesma transação, da mudança que os gerou.
- `OutboxDispatcher` e o `BackgroundService` `OutboxProcessor`: publicação em ordem, a cada 5 segundos, com entrega pelo menos uma vez, registro de tentativas e erro, e limite de 5 tentativas.
- `IOutboxPublisher` com uma implementação que registra no log; a seção `Outbox` do `appsettings.json` controla intervalo, lote e tentativas.
- Interface `IHasDomainEvents` no domínio, implementada por `AggregateRoot`.
- 6 testes novos (143 no total).

### Adicionado
- Política de autorização `order-owner`, baseada no recurso: o administrador acessa qualquer pedido; o cliente, só os dele (`OrderOwnerHandler`).
- Pedido de outro cliente responde `404`, igual a um pedido inexistente, para não revelar que o id existe.
- Na listagem, o cliente vê só os próprios pedidos, e pedir os de outro cliente dá `403`.
- 11 testes da política (137 no total): `401` em todas as rotas de pedido, `403`, `404` e o administrador vendo tudo.

### Corrigido
- A listagem de pedidos dava `500` quando a página vinha vazia (um cliente sem pedidos, num banco sem pedidos). Com nenhuma linha, o SQLite declara as colunas agregadas como BLOB, e o Dapper não conseguia montar o record posicional da linha. A linha virou uma classe com propriedades. O defeito existia desde o Dia 06 e apareceu nos testes novos da política.
- Autenticação JWT (Microsoft.AspNetCore.Authentication.JwtBearer 10.0.14): assinatura HMAC-SHA256, emissor, público e validade conferidos em cada requisição.
- `POST /api/v1/auth/token`, que troca e-mail e senha de uma conta de demonstração por um token de 60 minutos.
- Perfis `Admin` e `Customer`: cadastrar produto exige `Admin`; criar pedido exige `Customer`; as demais rotas de pedido exigem um token válido.
- Contas de demonstração em `demo-accounts.json` (só em desenvolvimento), com hash PBKDF2 do `PasswordHasher` do ASP.NET Core Identity.
- Opções `Jwt` validadas na partida: fora de desenvolvimento, sem `Jwt__SigningKey` com pelo menos 32 bytes, a API não sobe. Em desenvolvimento, sem chave configurada, uma chave nova é gerada a cada execução.
- Respostas `401` e `403` no mesmo formato ProblemDetails dos outros erros.
- 9 testes de autenticação (126 no total): 401, 403, token vencido, adulterado, de outra chave, de outro emissor e para outra API.

### Alterado
- O cliente do pedido vem do claim `sub` do token, e não mais do corpo da requisição.

## [0.2.0] - 2026-11-11

Segunda semana do roadmap: leitura com Dapper, Result pattern, testes da API, concorrência de estoque e logs estruturados.

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
- Serilog.AspNetCore 10.0.0 com logs estruturados: texto legível em desenvolvimento e JSON compacto nos outros ambientes, com níveis mínimos na seção `Serilog` do `appsettings.json`.
- `CorrelationIdMiddleware`: usa o `X-Correlation-Id` recebido (só letras, números, `.`, `_` e `-`, até 64 caracteres) ou cria um novo, devolve na resposta e coloca o id em todo log da requisição.
- Log de cada requisição HTTP com método, caminho, status e tempo (`UseSerilogRequestLogging`).
- Campo `correlationId` em todas as respostas ProblemDetails.
- 7 testes de correlation id e de logs estruturados (117 no total).

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
