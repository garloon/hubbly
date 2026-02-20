using FluentValidation;
using Hubbly.Api.Controllers;

namespace Hubbly.Api.Validators;

public class UpdateNicknameRequestValidator : AbstractValidator<UpdateNicknameRequest>
{
    public UpdateNicknameRequestValidator()
    {
        RuleFor(x => x.NewNickname)
            .NotEmpty()
            .WithMessage("Nickname cannot be empty")
            .MaximumLength(50)
            .WithMessage("Nickname cannot exceed 50 characters");
    }
}
