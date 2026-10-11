using FluentValidation;
using SupportDesk.Application.Contracts.Tickets;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketRequestValidator : AbstractValidator<EscalateTicketRequest>
{
    public EscalateTicketRequestValidator()
    {
        RuleFor(request => request.Reason).Cascade(CascadeMode.Stop)
            .NotEmpty().MaximumLength(500)
            .Must(reason => reason.Trim().Length >= 5)
            .WithMessage("Reason must contain at least 5 characters after trimming whitespace.");
    }
}
