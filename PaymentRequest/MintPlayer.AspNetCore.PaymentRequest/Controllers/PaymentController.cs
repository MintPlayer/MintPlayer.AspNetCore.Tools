using Microsoft.AspNetCore.Mvc;

namespace MintPlayer.AspNetCore.PaymentRequest.Controllers;

[Controller]
[Route("Payment")]
public class PaymentController : Controller
{
    [HttpGet("Overview")]
    public async Task<IActionResult> Overview()
    {
        return View();
    }


}
