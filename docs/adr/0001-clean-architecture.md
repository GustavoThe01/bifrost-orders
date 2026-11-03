# ADR 0001 — Clean Architecture

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
A Bifrost terá regras de pedido não triviais (estados, estoque, pagamento), integrações
externas e processamento em segundo plano. Quero que essas regras sejam testáveis
sem banco nem HTTP e que detalhes técnicos possam mudar sem reescrever o núcleo.

## Decisão
Separar a solução em quatro projetos, com dependências apontando para o centro:

```
Api ──► Application ──► Domain
Api ──► Infrastructure ──► Application
```

- **Domain** — entidades, value objects, eventos e regras. Sem dependências externas.
- **Application** — casos de uso (commands/queries), validações e interfaces (portas).
- **Infrastructure** — EF Core, Dapper, outbox, clientes HTTP.
- **Api** — endpoints, middlewares, autenticação e composition root.

A regra é verificada por testes (hoje via reflexão; NetArchTest no Dia 19).

## Alternativas consideradas
- **Projeto único com pastas** — mais simples, mas sem barreira de compilação entre camadas.
- **Vertical Slice Architecture** — ótima para CRUDs independentes; pode ser combinada
  dentro de Application (pastas por feature) sem abrir mão das camadas.

## Consequências
- ✅ Domínio e casos de uso testáveis isoladamente.
- ✅ Troca de banco ou de provedor de pagamento afeta apenas Infrastructure.
- ⚠️ Mais projetos e mais mapeamentos (DTO ↔ domínio) para manter.
