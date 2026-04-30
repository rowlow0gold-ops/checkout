using Microsoft.AspNetCore.Mvc;

namespace StoreServer.Controllers;

/// <summary>
/// Serves the store manager web dashboard at /admin
/// </summary>
[Route("admin")]
public class AdminController : Controller
{
    [HttpGet("weight-checks")]
    public IActionResult WeightChecks() => Content(AdminHtml, "text/html");

    private const string AdminHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8"/>
          <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
          <title>J Mart — Weight Check Monitor</title>
          <style>
            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
            body {
              font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
              background: #0D1117; color: #E6EDF3; min-height: 100vh;
            }
            header {
              background: #161B22; border-bottom: 1px solid #30363D;
              padding: 16px 32px; display: flex; align-items: center; gap: 16px;
            }
            .logo {
              background: #F59E0B; color: #0D1117; font-weight: 800;
              width: 36px; height: 36px; border-radius: 8px;
              display: flex; align-items: center; justify-content: center; font-size: 18px;
            }
            h1 { font-size: 18px; font-weight: 700; }
            .subtitle { font-size: 13px; color: #8B949E; margin-left: auto; }
            main { padding: 28px 32px; max-width: 1200px; margin: 0 auto; }

            /* Summary cards */
            .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 16px; margin-bottom: 28px; }
            .card {
              background: #161B22; border: 1px solid #30363D; border-radius: 10px;
              padding: 18px 20px;
            }
            .card-label { font-size: 11px; color: #8B949E; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 8px; }
            .card-value { font-size: 28px; font-weight: 700; }
            .card-value.green  { color: #3FB950; }
            .card-value.red    { color: #F85149; }
            .card-value.yellow { color: #D29922; }
            .card-value.blue   { color: #58A6FF; }

            /* Filters */
            .filters {
              display: flex; gap: 10px; align-items: center; flex-wrap: wrap;
              margin-bottom: 20px;
            }
            select, button {
              background: #21262D; color: #E6EDF3; border: 1px solid #30363D;
              border-radius: 6px; padding: 7px 12px; font-size: 13px; cursor: pointer;
            }
            select:focus, button:focus { outline: 2px solid #58A6FF; }
            button.primary { background: #2563EB; border-color: #2563EB; }
            button.primary:hover { background: #1D4ED8; }
            .filter-label { font-size: 12px; color: #8B949E; }

            /* Table */
            .table-wrap { overflow-x: auto; border-radius: 10px; border: 1px solid #30363D; }
            table { width: 100%; border-collapse: collapse; font-size: 13px; }
            thead { background: #161B22; }
            thead th {
              text-align: left; padding: 12px 16px;
              font-size: 11px; text-transform: uppercase; letter-spacing: 0.8px; color: #8B949E;
              border-bottom: 1px solid #30363D;
            }
            tbody tr { border-bottom: 1px solid #21262D; }
            tbody tr:last-child { border-bottom: none; }
            tbody tr:hover { background: #161B22; }
            td { padding: 11px 16px; vertical-align: middle; }
            .badge {
              display: inline-block; padding: 3px 10px; border-radius: 20px;
              font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;
            }
            .badge.pass           { background: #0d2818; color: #3FB950; border: 1px solid #238636; }
            .badge.fail           { background: #2d0f0f; color: #F85149; border: 1px solid #6e1313; }
            .badge.timeout        { background: #261d04; color: #D29922; border: 1px solid #9e6a03; }
            .badge.staff_override { background: #0d1a2b; color: #60A5FA; border: 1px solid #1e3a5f; }
            .terminal-dot {
              display: inline-block; width: 8px; height: 8px; border-radius: 50%;
              margin-right: 6px; background: #58A6FF;
            }
            .mono { font-family: 'Courier New', monospace; font-size: 12px; color: #8B949E; }
            .grams { font-size: 12px; color: #8B949E; }
            .empty { text-align: center; padding: 60px; color: #8B949E; font-size: 14px; }
            #status { font-size: 12px; color: #8B949E; margin-left: 8px; }

            /* PIN management */
            .section-title { font-size: 14px; font-weight: 700; color: #E6EDF3; margin-bottom: 12px; }
            .pin-panel {
              background: #161B22; border: 1px solid #30363D; border-radius: 10px;
              padding: 20px 24px; margin-bottom: 28px; display: flex;
              align-items: center; gap: 20px; flex-wrap: wrap;
            }
            .pin-panel label { font-size: 13px; color: #8B949E; }
            .pin-input {
              background: #21262D; color: #E6EDF3; border: 1px solid #30363D;
              border-radius: 6px; padding: 8px 14px; font-size: 18px; font-weight: 700;
              letter-spacing: 6px; width: 110px; text-align: center;
            }
            .pin-input:focus { outline: 2px solid #58A6FF; border-color: transparent; }
            #pin-msg { font-size: 13px; margin-left: 4px; }
            .pin-msg-ok  { color: #3FB950; }
            .pin-msg-err { color: #F85149; }
          </style>
        </head>
        <body>
          <header>
            <div class="logo">J</div>
            <h1>Weight Check Monitor</h1>
            <div class="subtitle">J Mart Self-Checkout / Store Manager</div>
          </header>
          <main>
            <!-- Staff PIN Management -->
            <div class="section-title">⚙ Store Settings</div>
            <div class="pin-panel">
              <label>Staff Settings PIN</label>
              <input id="pin-input" class="pin-input" type="password" maxlength="4" placeholder="••••"
                     inputmode="numeric" pattern="[0-9]*" autocomplete="off"/>
              <button class="primary" onclick="savePin()">Save PIN</button>
              <span id="pin-msg"></span>
            </div>

            <div class="section-title">📊 Weight Check Monitor</div>
            <div class="cards">
              <div class="card">
                <div class="card-label">Total Checks</div>
                <div class="card-value blue" id="stat-total">—</div>
              </div>
              <div class="card">
                <div class="card-label">Passed</div>
                <div class="card-value green" id="stat-pass">—</div>
              </div>
              <div class="card">
                <div class="card-label">Failed</div>
                <div class="card-value red" id="stat-fail">—</div>
              </div>
              <div class="card">
                <div class="card-label">Timed Out</div>
                <div class="card-value yellow" id="stat-timeout">—</div>
              </div>
              <div class="card">
                <div class="card-label">Pass Rate</div>
                <div class="card-value green" id="stat-rate">—</div>
              </div>
              <div class="card">
                <div class="card-label">High Risk (≥5)</div>
                <div class="card-value red" id="stat-highrisk">—</div>
              </div>
            </div>

            <div class="filters">
              <span class="filter-label">Terminal:</span>
              <select id="filter-terminal">
                <option value="">All</option>
                <option value="1">Terminal #1</option>
                <option value="2">Terminal #2</option>
                <option value="3">Terminal #3</option>
              </select>
              <span class="filter-label">Result:</span>
              <select id="filter-result">
                <option value="">All</option>
                <option value="pass">Pass</option>
                <option value="fail">Fail</option>
                <option value="timeout">Timeout</option>
              </select>
              <button class="primary" onclick="loadData()">Refresh</button>
              <span id="status"></span>
            </div>

            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Terminal</th>
                    <th>Product</th>
                    <th>Barcode</th>
                    <th>Expected</th>
                    <th>Actual</th>
                    <th>Difference</th>
                    <th>Result</th>
                    <th>Risk Score</th>
                    <th>Time</th>
                  </tr>
                </thead>
                <tbody id="table-body">
                  <tr><td colspan="9" class="empty">Loading…</td></tr>
                </tbody>
              </table>
            </div>
          </main>

          <script>
            // ── PIN management ──────────────────────────────────────────────
            async function loadPin() {
              try {
                const res  = await fetch('/api/settings/staff-pin');
                const data = await res.json();
                document.getElementById('pin-input').placeholder = '••••';
                document.getElementById('pin-input').dataset.current = data.pin;
              } catch(e) { /* server may be starting */ }
            }

            async function savePin() {
              const input = document.getElementById('pin-input');
              const msg   = document.getElementById('pin-msg');
              const pin   = input.value.trim();
              if (!/^\d{4}$/.test(pin)) {
                msg.textContent = '✕ PIN must be exactly 4 digits.';
                msg.className = 'pin-msg-err';
                return;
              }
              try {
                const res = await fetch('/api/settings/staff-pin', {
                  method: 'PUT',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({ pin }),
                });
                if (res.ok) {
                  msg.textContent = '✓ Saved! All terminals will use the new PIN.';
                  msg.className = 'pin-msg-ok';
                  input.value = '';
                  input.placeholder = '••••';
                } else {
                  const err = await res.json();
                  msg.textContent = '✕ ' + (err.error ?? 'Failed to save.');
                  msg.className = 'pin-msg-err';
                }
              } catch(e) {
                msg.textContent = '✕ Server unreachable.';
                msg.className = 'pin-msg-err';
              }
            }

            // Allow Enter key to submit
            document.addEventListener('DOMContentLoaded', () => {
              document.getElementById('pin-input').addEventListener('keydown', e => {
                if (e.key === 'Enter') savePin();
              });
              loadPin();
            });

            // ── Weight checks ───────────────────────────────────────────────
            async function loadData() {
              const terminal = document.getElementById('filter-terminal').value;
              const result   = document.getElementById('filter-result').value;
              const status   = document.getElementById('status');
              status.textContent = 'Loading…';

              let url = '/api/weight-checks?';
              if (terminal) url += `terminalId=${terminal}&`;
              if (result)   url += `result=${result}&`;

              try {
                const res  = await fetch(url);
                const data = await res.json();
                renderTable(data);
                updateStats(data);
                status.textContent = `Updated ${new Date().toLocaleTimeString()}`;
              } catch(e) {
                status.textContent = '⚠ Failed to load';
              }
            }

            function riskBadge(score) {
              if (score === 0)    return `<span style="color:#3FB950">0.000</span>`;
              if (score < 1.0)   return `<span style="color:#D29922;font-weight:600">${score.toFixed(3)} ⚠</span>`;
              if (score < 5.0)   return `<span style="color:#F85149;font-weight:600">${score.toFixed(3)} ⚠⚠</span>`;
              return               `<span style="color:#FF0000;font-weight:700;text-shadow:0 0 6px #f00">${score.toFixed(3)} 🚨</span>`;
            }

            function renderTable(rows) {
              const tbody = document.getElementById('table-body');
              if (rows.length === 0) {
                tbody.innerHTML = '<tr><td colspan="9" class="empty">No weight checks recorded yet.</td></tr>';
                return;
              }
              tbody.innerHTML = rows.map(r => {
                const diff    = r.result === 'timeout' ? '—' : `${r.actualGrams - r.expectedGrams > 0 ? '+' : ''}${r.actualGrams - r.expectedGrams} g`;
                const diffCol = r.result === 'fail' ? `<span style="color:#F85149">${diff}</span>` : `<span class="grams">${diff}</span>`;
                const time    = new Date(r.checkedAt).toLocaleString();
                return `<tr>
                  <td><span class="terminal-dot"></span>Terminal #${r.terminalId}</td>
                  <td>${r.productName}</td>
                  <td class="mono">${r.barcode}</td>
                  <td class="grams">${r.expectedGrams} g</td>
                  <td class="grams">${r.result === 'timeout' ? '—' : r.actualGrams + ' g'}</td>
                  <td>${diffCol}</td>
                  <td><span class="badge ${r.result}">${r.result}</span></td>
                  <td class="mono">${riskBadge(r.riskScore ?? 0)}</td>
                  <td class="mono">${time}</td>
                </tr>`;
              }).join('');
            }

            function updateStats(rows) {
              const total    = rows.length;
              const pass     = rows.filter(r => r.result === 'pass').length;
              const fail     = rows.filter(r => r.result === 'fail').length;
              const timeout  = rows.filter(r => r.result === 'timeout').length;
              const highrisk = rows.filter(r => (r.riskScore ?? 0) >= 5).length;
              const rate     = total > 0 ? Math.round(pass / total * 100) + '%' : '—';
              document.getElementById('stat-total').textContent    = total;
              document.getElementById('stat-pass').textContent     = pass;
              document.getElementById('stat-fail').textContent     = fail;
              document.getElementById('stat-timeout').textContent  = timeout;
              document.getElementById('stat-rate').textContent     = rate;
              document.getElementById('stat-highrisk').textContent = highrisk;
            }

            // Auto-refresh every 30s
            loadData();
            setInterval(loadData, 30000);
          </script>
        </body>
        </html>
        """;
}
