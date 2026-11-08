# ADR 0004 — Result pattern na fronteira da aplicação

- **Status:** Aceita
- **Data:** 2026-11-08

## Contexto
Até o Dia 06, toda falha virava exceção: `ValidationException`, `NotFoundException`, `ConflictException` e
`DomainException`. Um único `IExceptionHandler` traduzia cada uma para HTTP. Funcionava, mas tinha três problemas:

- a assinatura do handler mentia: `Task<Guid>` não dizia que o produto podia não existir;
- falha esperada (pedido não encontrado) e bug (conexão caiu) passavam pelo mesmo caminho;
- testar uma falha exigia `Assert.ThrowsAsync`, e não olhar para o valor devolvido.

## Decisão
- Todo command e query devolve **`Result<T>`**: um valor ou um `Error`, nunca os dois.
- `Error` tem um **código estável** (`order.not_found`, `product.sku_taken`), uma mensagem para pessoas e uma
  **categoria** (`Validation`, `NotFound`, `Conflict`, `BusinessRule`).
- A API traduz a categoria para HTTP num único lugar (`ResultExtensions`): **400, 404, 409 e 422**, sempre em
  ProblemDetails e com o código no campo `code`.
- O **domínio continua lançando `DomainException`** quando uma invariante seria quebrada. Um decorator do pipeline
  converte essa exceção em `Result` na fronteira da aplicação, então nenhum controller vê exceção de negócio.
- A validação do FluentValidation também vira `Result`, sem `ValidationException`.
- O `IExceptionHandler` fica só para o inesperado, sempre com 500 e sem detalhe interno.

Os decorators são genéricos em `TResult`. Para criarem uma falha do tipo certo sem reflexão, `Result<T>` implementa
`IFailureFactory<TSelf>`, com o membro estático abstrato `TSelf.Failure(Error)`.

## Alternativas consideradas
- **Manter só exceções** — menos código, mas com os três problemas acima.
- **Result também no domínio**, como faço no Plutus — deixaria cada método do agregado devolvendo `Result`.
  No Bifrost, as invariantes do agregado são poucas e quebrá-las é sempre erro de uso; manter a exceção no domínio
  deixa o modelo mais limpo, e a conversão acontece num ponto só.
- **Biblioteca pronta (FluentResults, ErrorOr, OneOf)** — resolveria, mas o tipo é pequeno e quero que o mapeamento
  para HTTP fique explícito no código.

## Consequências
- ✅ A assinatura conta a verdade: `Task<Result<Guid>>` avisa que pode falhar.
- ✅ O cliente da API trata o erro pelo `code`, sem depender do texto da mensagem.
- ✅ Os testes de falha conferem o valor devolvido e a categoria, sem capturar exceção.
- ✅ Falha esperada aparece no log como "recusado", separada dos erros de verdade.
- ⚠️ Os testes de integração precisam desembrulhar `.Value`, e ler `.Value` de uma falha lança exceção de propósito.
- ⚠️ Os dois projetos .NET do portfólio usam o Result de formas diferentes (no domínio no Plutus, na fronteira aqui).
  É uma escolha consciente, explicada neste ADR.
