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
