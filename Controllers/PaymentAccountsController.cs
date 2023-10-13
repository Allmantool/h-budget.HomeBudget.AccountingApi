using Microsoft.AspNetCore.Mvc;

namespace HomeBudget_Accounting_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PaymentAccountsController : ControllerBase
    {
        private readonly ILogger<PaymentAccountsController> _logger;

        public PaymentAccountsController(ILogger<PaymentAccountsController> logger)
        {
            _logger = logger;
        }

        [HttpGet(Name = "GetPaymentsAccounts")]
        public IEnumerable<PaymentAccount> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new PaymentAccount
            {
                Id = new Guid(),
                Agent = "Vtb Bank",
                Type = AccountType.Bank,
                Balance = 320.24m,
                Currency = "BYN",
                Description = "Common description"
            })
            .ToArray();
        }
    }
}