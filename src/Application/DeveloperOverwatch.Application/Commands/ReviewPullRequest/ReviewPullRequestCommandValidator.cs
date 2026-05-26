namespace DeveloperOverwatch.Application.Commands.ReviewPullRequest;

using FluentValidation;

public sealed class ReviewPullRequestCommandValidator : AbstractValidator<ReviewPullRequestCommand>
{
    public ReviewPullRequestCommandValidator()
    {
        RuleFor(x => x.Repo).NotEmpty();
        RuleFor(x => x.PrNumber).GreaterThan(0);
        RuleFor(x => x.Status).NotEmpty();
        RuleFor(x => x.ReviewedBy).NotEmpty();
    }
}
