const $ = (id) => document.getElementById(id);

let state = null;
let token = sessionStorage.getItem("goa2v2-token") || crypto.randomUUID();
let selectedMove = false;
let selectedMain = false;
let debugMode = null;
let legalCells = new Set();
let boardZoom = Number(localStorage.getItem("goa2v2-board-zoom") || 0.74);
let boardCentered = false;
let drag = null;
let suppressClick = false;

sessionStorage.setItem("goa2v2-token", token);
$("roomCode").value = localStorage.getItem("goa2v2-room") || "";
$("playerName").value = sessionStorage.getItem("goa2v2-name") || "";

async function request(path, body = {}) {
  const res = await fetch(path, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ code: $("roomCode").value.trim().toUpperCase(), token, ...body })
  });
  const data = await res.json();
  if (!res.ok) throw new Error(errorText(data.error));
  return data;
}

async function refresh() {
  const code = $("roomCode").value.trim().toUpperCase();
  if (!code) return;
  try {
    state = await request("/api/state");
    render();
  } catch (err) {
    $("phaseTitle").textContent = err.message;
  }
}

function render() {
  renderTop();
  renderPlayers();
  renderBoard();
  renderActions();
  renderHand();
  renderLog();
}

function renderTop() {
  $("phaseTitle").textContent = phaseName(state.phase);
  const played = publicPlayedCards();
  $("statusLine").innerHTML = `
    <span>第 ${state.round} 轮 / 第 ${state.turn} 回合 · 生命 蓝${state.lives.blue}/红${state.lives.red} · 破平${teamName(state.tiebreaker)} · 战线 蓝${state.frontMarks.blue}/红${state.frontMarks.red}</span>
    ${played ? `<span class="public-cards">${played}</span>` : ""}
  `;
}

function renderPlayers() {
  $("players").innerHTML = state.players.map((p) => {
    const hero = state.heroes[p.heroKey];
    const cls = [p.team];
    if (p.seat === state.meSeat) cls.push("me");
    if (p.seat === state.activeSeat) cls.push("active");
    const status = p.needsSpawn ? "待队长选出生点" : p.defeated ? "待复活" : p.played ? cardById(p.heroKey, p.played, p)?.name : p.selectedCardId ? "已暗选" : "未选牌";
    const passiveLines = passiveSummaryLines(p);
    return `
      <article class="${cls.join(" ")}">
        <button data-seat="${p.seat}" ${p.occupied && p.seat !== state.meSeat ? "disabled" : ""}>${p.seat === state.meSeat ? "我" : "入座"}</button>
        <div class="player-main">
          <strong>${escapeHtml(p.name || `座位 ${p.seat + 1}`)} · ${hero.name}</strong>
          <strong>${p.coins} 金 · 等级 ${p.heroLevel} · ${status}</strong>
          ${passiveLines.map((line) => `<span>${line}</span>`).join("")}
        </div>
        <button data-captain="${p.seat}" ${p.seat !== state.meSeat || state.phase !== "lobby" ? "disabled" : ""}>设队长</button>
        <div class="card-track">${allHeroCards(p).map((card) => cardDot(p, card)).join("")}</div>
      </article>
    `;
  }).join("");
  document.querySelectorAll("[data-seat]").forEach((btn) => btn.addEventListener("click", joinSeat));
  document.querySelectorAll("[data-captain]").forEach((btn) => btn.addEventListener("click", setCaptain));
}

function publicPlayedCards() {
  if (state.phase !== "reveal") return "";
  const seats = state.resolutionOrder?.length ? state.resolutionOrder : state.players.map((p) => p.seat);
  return seats.map((seat, index) => {
    const p = state.players[seat];
    const card = p?.played ? cardById(p.heroKey, p.played, p) : null;
    if (!p || !card) return "";
    return `${index + 1}. ${escapeHtml(p.name || `座位${seat + 1}`)}：${escapeHtml(card.name)}（先攻${statText(card, "initiative")}，${escapeHtml(card.text)}）`;
  }).filter(Boolean).join("　");
}

function allHeroCards(player) {
  return player.effectiveCards || state.cards[player.heroKey] || [];
}

function handDisplayCards(player) {
  const cards = player.hand.map((id) => cardById(player.heroKey, id, player)).filter(Boolean);
  return [...cards, ...allHeroCards(player).filter((card) => card.displayOnly)];
}

function cardDot(player, card) {
  const id = card.id;
  let cls = "";
  if (player.played === id) cls = "played";
  else if (player.selectedCardId === id) cls = "selected";
  else if (player.discard?.includes(id) || player.roundUsed?.includes(id)) cls = "discard";
  return `<span class="card-dot ${colorClass(card.color)} ${cls}" title="${escapeHtml(card.name)}：${escapeHtml(card.text)}">${escapeHtml(card.color.slice(0, 1))}</span>`;
}

function renderBoard() {
  const hexW = 58, hexH = 50, stepX = 44, stepY = 38, pad = 360;
  const minX = Math.min(...state.map.map((c) => c.x));
  const minY = Math.min(...state.map.map((c) => c.y));
  const maxLeft = Math.max(...state.map.map((c) => (c.x - minX) * stepX + (c.y - minY) * 22));
  const maxTop = Math.max(...state.map.map((c) => (c.y - minY) * stepY));
  $("board").style.width = `${maxLeft + hexW + pad * 2}px`;
  $("board").style.height = `${maxTop + hexH + pad * 2}px`;
  $("board").style.transform = `scale(${boardZoom})`;
  $("board").innerHTML = state.map.map((cell) => {
    const left = (cell.x - minX) * stepX + (cell.y - minY) * 22 + pad;
    const top = (cell.y - minY) * stepY + pad;
    const classes = ["hex", `region-${cell.region}`];
    if (cell.obstacle) classes.push("obstacle");
    if (cell.state?.includes("HeroSpawn")) classes.push("hero-spawn");
    if (legalCells.has(key(cell))) classes.push("legal");
    return `
      <button class="${classes.join(" ")}" data-x="${cell.x}" data-y="${cell.y}" data-state="${cell.state}" style="left:${left}px;top:${top}px" title="${cell.x},${cell.y} ${cell.region} ${cell.state}">
        <span class="coord">${cell.x},${cell.y}</span>
        <span class="label">${cellLabel(cell.state)}</span>
        ${piecesAt(cell.x, cell.y)}
      </button>
    `;
  }).join("");
  document.querySelectorAll(".hex").forEach((hex) => hex.addEventListener("click", onHexClick));
  if (!boardCentered) {
    boardCentered = true;
    requestAnimationFrame(() => {
      const viewport = $("boardViewport");
      viewport.scrollLeft = Math.max(0, (pad - 80) * boardZoom);
      viewport.scrollTop = Math.max(0, (pad - 90) * boardZoom);
    });
  }
}

function piecesAt(x, y) {
  const pieces = [];
  state.minions.forEach((m) => {
    if (m.x === x && m.y === y) pieces.push(`<button class="piece ${m.team} ${m.kind}" title="${teamName(m.team)}${minionName(m.kind)}">${minionShort(m.kind)}</button>`);
  });
  state.players.forEach((p) => {
    if (p.pos && !p.defeated && p.pos.x === x && p.pos.y === y) {
      const cls = ["piece", "hero", p.team];
      if (p.seat === state.meSeat) cls.push("me");
      if (p.seat === state.activeSeat) cls.push("active");
      pieces.push(`<button class="${cls.join(" ")}" title="${escapeHtml(p.name)} ${state.heroes[p.heroKey].name}">${state.heroes[p.heroKey].name.slice(0, 1)}</button>`);
    }
  });
  return `<span class="stack">${pieces.join("")}</span>`;
}

async function onHexClick(event) {
  if (suppressClick) {
    suppressClick = false;
    return;
  }
  const hex = event.currentTarget;
  const me = myPlayer();
  if (!me) return;
  const x = Number(hex.dataset.x), y = Number(hex.dataset.y);
  try {
    if (debugMode) {
      state = await request(debugMode === "teleport" ? "/api/debug/teleport" : "/api/debug/kill", { x, y });
      debugMode = null;
      render();
      return;
    }
    if ((["lobby", "planning"].includes(state.phase) || (state.phase === "reveal" && state.activeSeat === me.seat && me.needsSpawn)) && hex.dataset.state?.includes("HeroSpawn")) {
      const selfRespawn = state.phase === "reveal" && state.activeSeat === me.seat && me.needsSpawn;
      if (!selfRespawn && state.captains[me.team] !== me.seat) {
        setHint("只有本队队长能选择出生点。");
        return;
      }
      state = await request("/api/spawn", { x, y });
      render();
      return;
    }
    if (state.phase === "minionChoice" && state.activeSeat === me.seat) {
      state = await request("/api/remove-minion", { x, y });
      render();
      return;
    }
    if (selectedMain) {
      if (!legalCells.has(`${x},${y}`)) {
        setHint("请选择绿色闪烁的主要动作目标。");
        return;
      }
      state = await request("/api/main-action", { x, y });
      selectedMain = false;
      legalCells.clear();
      render();
      return;
    }
    if (selectedMove) {
      if (!legalCells.has(`${x},${y}`)) {
        setHint("请选择绿色闪烁的合法移动格。");
        return;
      }
      state = await request("/api/move", { x, y });
      selectedMove = false;
      legalCells.clear();
      render();
    }
  } catch (err) {
    alert(err.message);
  }
}

function renderActions() {
  const me = myPlayer();
  if (!me) {
    $("actions").innerHTML = `<span class="muted">先在左侧入座。</span>`;
    return;
  }
  const active = state.phase === "reveal" && state.activeSeat === me.seat;
  if (state.pendingCaptainChoice && state.activeSeat === me.seat) {
    $("actions").innerHTML = `
      <span class="muted">${escapeHtml(state.pendingCaptainChoice.reason)}，请选择先结算者</span>
      ${state.pendingCaptainChoice.seats.map((seat) => {
        const p = state.players[seat];
        const c = p.played ? cardById(p.heroKey, p.played, p) : null;
        return `<button data-choose-active="${seat}">${escapeHtml(p.name || `座位${seat + 1}`)} ${c ? escapeHtml(c.name) : ""}</button>`;
      }).join("")}
    `;
    document.querySelectorAll("[data-choose-active]").forEach((btn) => btn.addEventListener("click", chooseActive));
    return;
  }
  if (state.phase === "minionChoice" && state.activeSeat === me.seat) {
    $("actions").innerHTML = `<span class="muted">请选择本队要移除的小兵，重型兵最后才能移除。</span>`;
    return;
  }
  if (state.phase === "upgrade" && state.activeSeat === me.seat) {
    $("actions").innerHTML = upgradeButtons(me);
    document.querySelectorAll("[data-upgrade]").forEach((btn) => btn.addEventListener("click", chooseUpgrade));
    return;
  }
  if (state.phase === "defense" && state.activeSeat === me.seat) {
    const damage = state.pendingDefense?.damage ?? 0;
    const defenseCards = me.hand.map((id) => cardById(me.heroKey, id, me)).filter((c) => (c.defense || 0) >= damage);
    $("actions").innerHTML = `
      <span class="muted">受到 ${damage} 点伤害，弃置防御牌或死亡</span>
      ${defenseCards.map((c) => `<button data-defend="${c.id}">弃 ${escapeHtml(c.name)} 防${c.defense}</button>`).join("")}
      <button id="dieBtn">死亡</button>
    `;
    document.querySelectorAll("[data-defend]").forEach((btn) => btn.addEventListener("click", defend));
    $("dieBtn")?.addEventListener("click", async () => {
      state = await request("/api/defend", {});
      render();
    });
    return;
  }
  if (active && me.needsSpawn) {
    $("actions").innerHTML = `<span class="muted">你已死亡，先点击本方英雄出生点复活，再执行这张牌。</span>`;
    return;
  }
  $("actions").innerHTML = `
    <button id="mainBtn" ${!active || me.actionTaken ? "disabled" : ""}>主要动作</button>
    <button id="moveBtn" ${!active || me.actionTaken || currentCard()?.movement === undefined ? "disabled" : ""}>次要移动</button>
    <button id="skipBtn" ${!active || me.actionTaken ? "disabled" : ""}>放弃效果</button>
    <button id="finishBtn" class="primary" ${!active ? "disabled" : ""}>确认结束</button>
  `;
  $("mainBtn")?.addEventListener("click", () => {
    selectedMain = true;
    selectedMove = false;
    legalCells = legalMainCells(me);
    setHint("点击绿色闪烁格执行主要动作。");
    renderBoard();
  });
  $("moveBtn")?.addEventListener("click", () => {
    selectedMove = true;
    selectedMain = false;
    legalCells = legalMoveCells(me);
    setHint("点击绿色闪烁格移动。");
    renderBoard();
  });
  $("skipBtn")?.addEventListener("click", async () => {
    state = await request("/api/skip-action");
    selectedMain = false;
    selectedMove = false;
    legalCells.clear();
    render();
  });
  $("finishBtn")?.addEventListener("click", async () => {
    state = await request("/api/finish");
    selectedMain = false;
    selectedMove = false;
    legalCells.clear();
    render();
  });
}

function renderHand() {
  const me = myPlayer();
  if (!me) {
    $("heroPanel").innerHTML = `<strong>未入座</strong><span>每个标签页选择一个座位。</span>`;
    $("hand").innerHTML = "";
    return;
  }
  const hero = state.heroes[me.heroKey];
  $("heroPanel").innerHTML = `<strong>${hero.name}</strong><span>${teamName(me.team)} · ${me.coins} 金 · 等级 ${me.heroLevel}</span><span>${me.needsSpawn ? "等待队长选出生点" : me.pos ? `位置 ${me.pos.x},${me.pos.y}` : "未在场"}</span>`;
  $("handHint").textContent = state.phase === "planning" ? "暗选 1 张牌。" : "";
  $("hand").innerHTML = handDisplayCards(me).map((c) => {
    return `
      <article class="card ${colorClass(c.color)}">
        <header class="card-head"><strong>${escapeHtml(c.name)}</strong><span>${c.color} · 先攻${statText(c, "initiative")}</span></header>
        <div class="stats">${stat("防", c, "defense")}${stat("移", c, "movement")}${stat("攻", c, "attack")}</div>
        <p>${escapeHtml(c.text)}</p>
        <button data-select="${c.id}" ${state.phase !== "planning" || c.displayOnly ? "disabled" : ""}>${c.displayOnly ? "占位" : "选择"}</button>
      </article>
    `;
  }).join("");
  document.querySelectorAll("[data-select]").forEach((btn) => btn.addEventListener("click", selectCard));
}

function renderLog() {
  $("log").innerHTML = state.log.map((line) => `<div>${escapeHtml(line)}</div>`).join("");
}

function legalMoveCells(me) {
  const c = currentCard();
  if (!c || !me.pos) return new Set();
  const occupied = new Set([
    ...state.players.filter((p) => p.pos && !p.defeated).map((p) => key(p.pos)),
    ...state.minions.map((m) => key(m)),
  ]);
  return new Set(state.map
    .filter((cell) => !cell.obstacle && !occupied.has(key(cell)))
    .filter((cell) => canFastTravel(me, cell) || walkDistance(me.pos, cell, occupied, Boolean(c.canPhaseThroughWalls)) <= c.movement)
    .map(key));
}

function legalMainCells(me) {
  const c = currentCard();
  if (!c || !me.pos) return new Set();
  if (c.primary === "killMinion") {
    return new Set(state.minions
      .filter((m) => m.team !== me.team && hexDistance(me.pos, m) === 1)
      .filter((m) => m.kind !== "heavy" || !state.minions.some((other) => other.team === m.team && other.kind !== "heavy"))
      .map(key));
  }
  if (c.primary === "attackHero") {
    return new Set(state.players
      .filter((p) => p.team !== me.team && p.pos && !p.defeated && hexDistance(me.pos, p.pos) === 1)
      .map((p) => key(p.pos)));
  }
  if (c.primary === "attackAny") {
    return new Set([
      ...state.minions.filter((m) => m.team !== me.team).map(key),
      ...state.players.filter((p) => p.team !== me.team && p.pos && !p.defeated).map((p) => key(p.pos)),
    ]);
  }
  return new Set();
}

function walkDistance(start, target, occupied, canPhase) {
  if (key(start) === key(target)) return 0;
  const seen = new Set([key(start)]);
  const queue = [{ x: start.x, y: start.y, steps: 0 }];
  for (const cur of queue) {
    for (const n of neighbors(cur)) {
      const k = key(n);
      if (seen.has(k)) continue;
      const cell = cellAt(n.x, n.y);
      if (!cell) continue;
      if (cell.obstacle && !canPhase) continue;
      if (occupied.has(k) && k !== key(target)) continue;
      if (k === key(target)) return cur.steps + 1;
      seen.add(k);
      queue.push({ x: n.x, y: n.y, steps: cur.steps + 1 });
    }
  }
  return Infinity;
}

function canFastTravel(me, target) {
  const cur = cellAt(me.pos.x, me.pos.y);
  return !!cur && regionsTouch(cur.region, target.region) && !enemyInRegion(me.team, cur.region) && !enemyInRegion(me.team, target.region);
}

function regionsTouch(a, b) {
  if (!a || !b) return false;
  if (a === b) return true;
  return state.map.some((cell) => cell.region === a && neighbors(cell).some((n) => cellAt(n.x, n.y)?.region === b));
}

function enemyInRegion(team, region) {
  return state.players.some((p) => p.pos && p.team !== team && !p.defeated && cellAt(p.pos.x, p.pos.y)?.region === region) ||
    state.minions.some((m) => m.team !== team && cellAt(m.x, m.y)?.region === region);
}

function neighbors(cell) {
  return [{ x: cell.x + 1, y: cell.y }, { x: cell.x + 1, y: cell.y - 1 }, { x: cell.x, y: cell.y - 1 }, { x: cell.x - 1, y: cell.y }, { x: cell.x - 1, y: cell.y + 1 }, { x: cell.x, y: cell.y + 1 }];
}

function cellAt(x, y) {
  return state.map.find((c) => c.x === x && c.y === y);
}

function hexDistance(a, b) {
  const dx = a.x - b.x, dy = a.y - b.y, dz = -a.x - a.y - (-b.x - b.y);
  return (Math.abs(dx) + Math.abs(dy) + Math.abs(dz)) / 2;
}

async function joinSeat(event) {
  try {
    const seat = Number(event.currentTarget.dataset.seat);
    const name = $("playerName").value.trim() || `玩家${seat + 1}`;
    sessionStorage.setItem("goa2v2-name", name);
    const data = await request("/api/join", { seat, name });
    token = data.token;
    sessionStorage.setItem("goa2v2-token", token);
    state = data.state;
    render();
  } catch (err) { alert(err.message); }
}

async function setCaptain() {
  try {
    state = await request("/api/captain");
    render();
  } catch (err) { alert(err.message); }
}

async function selectCard(event) {
  try {
    state = await request("/api/select", { cardId: event.currentTarget.dataset.select });
    render();
  } catch (err) { alert(err.message); }
}

async function defend(event) {
  try {
    state = await request("/api/defend", { cardId: event.currentTarget.dataset.defend });
    render();
  } catch (err) { alert(err.message); }
}

async function chooseActive(event) {
  try {
    state = await request("/api/choose-active", { seat: Number(event.currentTarget.dataset.chooseActive) });
    render();
  } catch (err) { alert(err.message); }
}

async function chooseUpgrade(event) {
  try {
    const [color, direction] = event.currentTarget.dataset.upgrade.split(":");
    state = await request("/api/upgrade", { color, direction });
    render();
  } catch (err) { alert(err.message); }
}

$("createBtn").addEventListener("click", async () => {
  const res = await fetch("/api/create", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ token, name: $("playerName").value.trim() || "玩家1" }),
  });
  const data = await res.json();
  if (data.token) {
    token = data.token;
    sessionStorage.setItem("goa2v2-token", token);
  }
  $("roomCode").value = data.code;
  localStorage.setItem("goa2v2-room", data.code);
  state = data.state || await request("/api/state");
  render();
});

$("identityBtn").addEventListener("click", async () => {
  token = crypto.randomUUID();
  sessionStorage.setItem("goa2v2-token", token);
  await refresh();
});

$("roomCode").addEventListener("change", () => {
  const code = $("roomCode").value.trim().toUpperCase();
  $("roomCode").value = code;
  localStorage.setItem("goa2v2-room", code);
  refresh();
});

$("playerName").addEventListener("change", () => sessionStorage.setItem("goa2v2-name", $("playerName").value.trim()));

$("startBtn").addEventListener("click", async () => {
  try {
    state = await request("/api/start");
    render();
  } catch (err) { alert(err.message); }
});

$("debugCoinBtn").addEventListener("click", async () => {
  try {
    state = await request("/api/debug/coin");
    render();
  } catch (err) { alert(err.message); }
});

$("debugCoins99Btn").addEventListener("click", async () => {
  try {
    state = await request("/api/debug/coins99");
    render();
  } catch (err) { alert(err.message); }
});

$("debugRoundBtn").addEventListener("click", async () => {
  try {
    state = await request("/api/debug/end-round");
    render();
  } catch (err) { alert(err.message); }
});

$("debugTeleportBtn").addEventListener("click", () => {
  debugMode = "teleport";
  selectedMove = false;
  selectedMain = false;
  setHint("调试传送：点击任意可站立格。");
});

$("debugKillBtn").addEventListener("click", () => {
  debugMode = "kill";
  selectedMove = false;
  selectedMain = false;
  setHint("调试击杀：点击任意单位所在格。");
});

$("boardViewport").addEventListener("wheel", (event) => {
  event.preventDefault();
  boardZoom = clamp(boardZoom + (event.deltaY < 0 ? 0.08 : -0.08), 0.45, 1.8);
  localStorage.setItem("goa2v2-board-zoom", String(boardZoom));
  $("board").style.transform = `scale(${boardZoom})`;
}, { passive: false });

$("boardViewport").addEventListener("pointerdown", (event) => {
  if (event.target.closest(".piece")) return;
  drag = { x: event.clientX, y: event.clientY, left: $("boardViewport").scrollLeft, top: $("boardViewport").scrollTop };
  suppressClick = false;
  $("boardViewport").classList.add("dragging");
});

$("boardViewport").addEventListener("pointermove", (event) => {
  if (!drag) return;
  if (Math.abs(event.clientX - drag.x) + Math.abs(event.clientY - drag.y) > 6) suppressClick = true;
  $("boardViewport").scrollLeft = drag.left - (event.clientX - drag.x);
  $("boardViewport").scrollTop = drag.top - (event.clientY - drag.y);
});

["pointerup", "pointercancel", "mouseleave"].forEach((name) => $("boardViewport").addEventListener(name, () => {
  drag = null;
  $("boardViewport").classList.remove("dragging");
}));

function setHint(text) { $("handHint").textContent = text; $("boardHelp").textContent = text; }
function myPlayer() { return state?.players?.[state.meSeat]; }
function currentCard() { const me = myPlayer(); return me?.played ? cardById(me.heroKey, me.played, me) : null; }
function cardById(heroKey, id, player = null) {
  return (player?.effectiveCards || state.cards[heroKey]).find((c) => c.id === id);
}
function key(c) { return `${c.x},${c.y}`; }
function stat(label, card, field) {
  if (card[field] === null || card[field] === undefined) return "";
  return `<span>${label}${statText(card, field)}</span>`;
}
function statText(card, field) {
  const base = card.baseStats?.[field] ?? card[field];
  const bonus = card.bonusStats?.[field] || 0;
  return bonus ? `${base}+${bonus}` : `${base}`;
}
function passiveSummaryLines(player) {
  const b = player.bonuses || {};
  const parts = [];
  if (b.damage) parts.push(`伤害+${b.damage}`);
  if (b.defense) parts.push(`防御+${b.defense}`);
  if (!parts.length) return ["无被动", "", ""];
  return [parts.slice(0, 2).join("、"), parts.slice(2, 4).join("、"), parts.slice(4).join("、")];
}

function upgradeButtons(player) {
  const colors = availableUpgradeColors(player);
  if (!colors.length && !player.hasUltimate) return `<span class="muted">已满足大招条件，等待系统授予。</span>`;
  const names = { red: "红", green: "绿", blue: "蓝" };
  return `
    <span class="muted">选择一张红/绿/蓝卡升级。三色均 2 级后才能升 3，三色均 3 后获得紫色大招。</span>
    ${colors.map((color) => {
      const next = player.skills[color] + 1;
      const value = next - 1;
      return `
        <button data-upgrade="${color}:initiative">${names[color]}升${next}：先攻+${value} / 被动伤害+${value}</button>
        <button data-upgrade="${color}:movement">${names[color]}升${next}：移动+${value} / 被动防御+${value}</button>
      `;
    }).join("")}
  `;
}

function availableUpgradeColors(player) {
  const colors = ["red", "green", "blue"];
  if (colors.every((color) => player.skills[color] >= 2)) {
    return colors.filter((color) => player.skills[color] < 3);
  }
  return colors.filter((color) => player.skills[color] < 2);
}
function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }
function teamName(team) { return team === "blue" ? "蓝方" : "红方"; }
function phaseName(phase) { return { lobby: "大厅/选出生点", planning: "暗选", reveal: "翻牌结算", defense: "防御响应", minionChoice: "兵线移除", upgrade: "升级", ended: "游戏结束" }[phase] || phase; }
function colorClass(color) { return { 金: "gold", 银: "silver", 红: "red", 蓝: "blue", 绿: "green", 紫: "purple" }[color] || ""; }
function cellLabel(state) { return { blueHeroSpawn: "蓝英", redHeroSpawn: "红英", blueMeleeSpawn: "蓝近", blueRangedSpawn: "蓝远", blueHeavySpawn: "蓝重", redMeleeSpawn: "红近", redRangedSpawn: "红远", redHeavySpawn: "红重", terrain: "阻" }[state] || ""; }
function minionName(kind) { return { melee: "近战小兵", ranged: "远程小兵", heavy: "重型小兵" }[kind] || "小兵"; }
function minionShort(kind) { return { melee: "近", ranged: "远", heavy: "重" }[kind] || "兵"; }
function errorText(value) {
  return {
    "Room not found": "找不到房间",
    "Join a seat first": "请先入座",
    "Seat already taken": "该座位已被占用",
    "Need all 4 seats filled": "需要 4 个座位都入座",
    "Need captains choose spawns": "需要双方队长先选择英雄出生点",
    "Captain only": "只有队长能选择出生/复活点",
    "Invalid hero spawn": "请选择本队英雄出生点",
    "Spawn already occupied": "该出生点已被占用",
    "Not in planning phase": "当前不是暗选阶段",
    "Card not in hand": "这张牌不在手牌中",
    "Not your resolution": "还没轮到你结算",
    "Action already used": "本次行动已经使用",
    "Invalid destination": "目标格不可移动",
    "Too far for card movement": "超过本牌移动距离",
  }[value] || value || "请求失败";
}
function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#039;" }[ch]));
}

setInterval(refresh, 1000);
refresh();
