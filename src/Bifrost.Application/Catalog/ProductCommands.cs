using Bifrost.Application.Abstractions;
using Bifrost.Application.Messaging;
using Bifrost.Domain.Catalog;
using Bifrost.Domain.ValueObjects;
using FluentValidation;

namespace Bifrost.Application.Catalog;

public sealed record CreateProductCommand(
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    int StockQuantity) : ICommand<Guid>;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(c => c.Sku).NotEmpty().Length(3, 20);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Price).GreaterThan(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.StockQuantity).GreaterThanOrEqualTo(0);
    }
}

internal sealed class CreateProductHandler(IProductRepository products, IUnitOfWork unitOfWork)
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var product = Product.Create(
            command.Sku,
            command.Name,
            Money.Create(command.Price, command.Currency),
            command.StockQuantity);

        if (await products.SkuExistsAsync(product.Sku, cancellationToken))
        {
            throw new ConflictException($"Já existe um produto com o SKU {product.Sku}.");
        }

        products.Add(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return product.Id.Value;
    }
}
