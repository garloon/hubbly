using FluentValidation;
using Hubbly.Api.Controllers;

namespace Hubbly.Api.Validators;

public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .WithMessage("Refresh token is required");

        RuleFor(x => x.DeviceId)
            .NotEmpty()
            .WithMessage("DeviceId is required");
    }
}
