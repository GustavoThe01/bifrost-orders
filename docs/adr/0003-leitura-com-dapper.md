# ADR 0003 — Leituras críticas com Dapper, escrita com EF Core

- **Status:** Aceita
- **Data:** 2026-11-07

## Contexto
No Dia 04 separei commands e queries, mas os dois lados ainda passavam pelo EF Core. Para ler um pedido,
eu carregava o agregado inteiro com os itens e montava o DTO em memória. Para listar pedidos com o total,
o caminho natural no EF seria carregar os itens de cada pedido só para somá-los.

O lado de leitura não precisa de agregado, de invariantes nem de change tracking. Precisa de poucas colunas,
de somas feitas no banco e de paginação.

## Decisão
- **Escrita continua no EF Core**: agregados, change tracking e uma transação por `SaveChanges`.
- **Leituras de pedido vão para o Dapper**, com SQL escrito à mão:
  - `GET /api/v1/orders/{id}`: duas consultas numa ida ao banco (`QueryMultiple`), cabeçalho e itens.
  - `GET /api/v1/orders`: total somado em centavos no próprio SQL (`SUM(unit_price_cents * quantity)`),
    filtros por cliente e status, `LIMIT`/`OFFSET` e a contagem total na mesma chamada.
- **O catálogo continua no EF Core**, com `AsNoTracking` e projeção no `Select`, porque é uma leitura
  simples e a projeção já faz o SQL buscar só as colunas usadas.
- O Dapper usa **a mesma conexão do `DbContext`**. Não existe uma segunda connection string, e a leitura
  enxerga exatamente o banco que a escrita usa.
- A listagem ordena pelo **id**, que é UUID v7 e cresce com o tempo. Com isso não precisei criar agora
  uma coluna de data de criação só para ordenar.

## Alternativas consideradas
- **Só EF Core, com projeções** — resolveria boa parte, mas a soma por pedido com paginação gera SQL que eu
  não controlo, e o objetivo do projeto é mostrar CQRS com modelos de leitura de verdade.
- **Views ou tabelas de leitura atualizadas por eventos** — é o próximo passo natural do CQRS, mas
  depende do Outbox, que entra no Dia 13.
- **Dapper também na escrita** — é o que faço no Plutus. Aqui eu perderia o change tracking e o mapeamento
  do agregado, que são o motivo de usar EF Core neste projeto.

## Consequências
- ✅ A leitura de pedidos não instancia agregados e faz a soma no banco.
- ✅ O SQL de leitura fica visível no código, fácil de revisar e de levar para um `EXPLAIN`.
- ✅ Os testes de integração rodam o SQL contra o mesmo SQLite das migrations, então uma coluna renomeada
  quebra o teste, e não a produção.
- ⚠️ O SQL não acompanha o modelo do EF automaticamente: se eu renomear uma coluna na configuração,
  preciso lembrar das consultas do Dapper. Os testes de integração são a rede de segurança.
- ⚠️ Guid fica como TEXT em maiúsculas no SQLite (convenção do EF Core); os parâmetros do Dapper precisam
  seguir o mesmo formato. Isso muda quando o banco for PostgreSQL, com tipo `uuid` nativo.
- ⚠️ `LIMIT`/`OFFSET` fica lento em páginas muito distantes. Se a listagem crescer, troco por paginação
  por cursor (o id v7 já serve de cursor).
