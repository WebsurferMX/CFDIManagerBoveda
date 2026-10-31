namespace CFDIManager.Web.Services.Interfaces;
using CFDIManager.Web.Models;

public interface ISatAuthService
{
    Task<SatAuthResult> ValidarAsync(long empresaId);
}