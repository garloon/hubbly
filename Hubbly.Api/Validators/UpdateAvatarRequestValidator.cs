using FluentValidation;
using Hubbly.Api.Controllers;

namespace Hubbly.Api.Validators;

public class UpdateAvatarRequestValidator : AbstractValidator<UpdateAvatarRequest>
{
    public UpdateAvatarRequestValidator()
    {
        RuleFor(x => x.AvatarConfigJson)
            .NotEmpty()
            .WithMessage("Avatar config cannot be empty")
            .MaximumLength(2000)
            .WithMessage("Avatar config cannot exceed 2000 characters");
    }
}
