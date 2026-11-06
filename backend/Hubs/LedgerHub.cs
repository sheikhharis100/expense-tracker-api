using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ExpenseTracker.Api.Hubs;

// Clients only listen. SignalR routes messages per user via the NameIdentifier claim,
// so every device signed in to the same account receives the same "ledgerChanged" events.
[Authorize]
public class LedgerHub : Hub
{
}
