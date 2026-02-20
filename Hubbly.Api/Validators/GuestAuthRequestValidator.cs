using FluentValidation;
using Hubbly.Api.Controllers;

namespace Hubbly.Api.Validators;

public class GuestAuthRequestValidator : AbstractValidator<GuestAuthRequest>
{
    public GuestAuthRequestValidator()
    {
        RuleFor(x => x.DeviceId)
            .NotEmpty()
            .WithMessage("DeviceId is required");
    }
}
