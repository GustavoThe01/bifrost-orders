# ADR 0002 — Dispatcher próprio de commands/queries em vez de MediatR

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
Para aplicar CQRS precisamos despachar commands e queries para seus handlers e
executar comportamentos transversais (validação, logging, transação) em pipeline.
O MediatR é a escolha tradicional, mas passou a ter licença comercial a partir da versão 13.

## Decisão
Implementar um **dispatcher próprio e enxuto**: interfaces `ICommandHandler<TCommand, TResult>`
e `IQueryHandler<TQuery, TResult>`, registro via DI e **decorators** para os comportamentos de pipeline.

## Alternativas consideradas
- **MediatR** — maduro e conhecido, porém com licença comercial e "mágica" que esconde o fluxo.
- **Wolverine / Brighter** — poderosos, mas excedem a necessidade do projeto.
- **Chamar serviços diretamente** — simples, mas perde o pipeline uniforme.

## Consequências
- ✅ Nenhuma dependência de licença; o fluxo fica explícito e fácil de explicar em entrevista.
- ✅ Demonstra domínio de DI, generics e do padrão Decorator.
- ⚠️ Mais código nosso para manter e testar.
