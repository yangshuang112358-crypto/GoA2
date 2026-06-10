const $ = (id) => document.getElementById(id);

let state = null;
let token = sessionStorage.getItem("goa2v2-token") || crypto.randomUUID();
let selectedMove = false;
let selectedFastMove = false;
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
  if (document.activeElement?.matches("[data-hero-select]")) return;
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
  const upgrade = upgradePreviewCards();
  $("statusLine").innerHTML = `
    <span>第 ${state.round} 轮 / 第 ${state.turn} 回合 · 生命 蓝${state.lives.blue}/红${state.lives.red} · 决策币${teamName(state.decisionCoin || state.tiebreaker)} · 战线 蓝${state.frontMarks.blue}/红${state.frontMarks.red}</span>
    ${played ? `<div class="public-cards">${played}</div>` : ""}
    ${upgrade ? `<div class="public-cards upgrade-preview">${upgrade}</div>` : ""}
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
        ${p.seat === state.meSeat && state.phase === "lobby" ? heroSelect(p) : ""}
        <div class="card-track">${statusCards(p).map((card) => cardDot(p, card)).join("")}</div>
      </article>
    `;
  }).join("");
  document.querySelectorAll("[data-seat]").forEach((btn) => btn.addEventListener("click", joinSeat));
  document.querySelectorAll("[data-captain]").forEach((btn) => btn.addEventListener("click", setCaptain));
  document.querySelectorAll("[data-hero-select]").forEach((select) => select.addEventListener("change", selectHero));
}

function publicPlayedCards() {
  if (!["reveal", "defense"].includes(state.phase)) return "";
  const entries = currentTrickEntries();
  return entries.map((entry) => {
    const p = state.players[entry.seat];
    const card = p ? cardById(p.heroKey, entry.cardId, p) : null;
    if (!p || !card) return "";
    return cardHtml(card, {
      owner: p.name || `座位${entry.seat + 1}`,
      compact: true,
      resolved: entry.resolved,
      button: ""
    });
  }).filter(Boolean).join("");
}

function currentTrickEntries() {
  if (state.currentTrick?.length) {
    const order = state.resolutionOrder || [];
    return [...state.currentTrick].sort((a, b) => {
      const pa = state.players[a.seat], pb = state.players[b.seat];
      const ca = pa ? cardById(pa.heroKey, a.cardId, pa) : null;
      const cb = pb ? cardById(pb.heroKey, b.cardId, pb) : null;
      const ai = order.indexOf(a.seat), bi = order.indexOf(b.seat);
      if (ai !== -1 || bi !== -1) return (ai === -1 ? 999 : ai) - (bi === -1 ? 999 : bi);
      return ((cb?.initiative ?? 0) - (ca?.initiative ?? 0)) || a.seat - b.seat;
    });
  }
  return (state.resolutionOrder || []).map((seat) => ({ seat, cardId: state.players[seat]?.played, resolved: state.players[seat]?.resolved })).filter((x) => x.cardId);
}

function upgradePreviewCards() {
  if (state.phase !== "upgrade") return "";
  if (state.meSeat !== state.activeSeat) return "";
  const p = state.players[state.activeSeat];
  if (!p) return "";
  return upgradeCandidateCards(p).map(({ card, passive }) => cardHtml(card, {
    compact: true,
    upgradePassive: passive,
    button: `<button data-upgrade="${colorKey(card.color)}:${card.id}">${card.color}升级至${card.level}级</button>`
  })).join("");
}

function allHeroCards(player) {
  return player.effectiveCards || state.cards[player.heroKey] || [];
}

function heroSelect(player) {
  const options = Object.entries(state.heroes).map(([key, hero]) => {
    const suffix = hero.implemented ? "" : "（默认牌）";
    return `<option value="${key}" ${player.heroKey === key ? "selected" : ""}>${escapeHtml(hero.name)}${suffix}</option>`;
  }).join("");
  return `<select class="hero-select" data-hero-select="${player.seat}">${options}</select>`;
}

function handDisplayCards(player) {
  const cards = player.hand.map((id) => cardById(player.heroKey, id, player)).filter(Boolean);
  return [...cards, ...allHeroCards(player).filter((card) => card.displayOnly)];
}

function statusCards(player) {
  return (player.hand || []).map((id) => cardById(player.heroKey, id, player)).filter((card) => card && !card.displayOnly).slice(0, 5);
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
    if (selectedMove || selectedFastMove) {
      if (!legalCells.has(`${x},${y}`)) {
        setHint("请选择绿色闪烁的合法移动格。");
        return;
      }
      state = await request("/api/move", { x, y });
      selectedMove = false;
      selectedFastMove = false;
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
    $("actions").innerHTML = `<span class="muted">在上方出牌区选择升级方向。</span>`;
    document.querySelectorAll("[data-upgrade]").forEach((btn) => btn.addEventListener("click", chooseUpgrade));
    return;
  }
  if (state.phase === "defense" && state.activeSeat === me.seat) {
    const damage = state.pendingDefense?.damage ?? 0;
    const defenseCards = me.hand
      .filter((id) => !me.discard?.includes(id) && !me.roundUsed?.includes(id))
      .map((id) => cardById(me.heroKey, id, me))
      .filter((c) => c.exclamation || (c.defense || 0) >= damage || c.primaryCategory === "防御");
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
    <button id="moveBtn" ${!active || me.actionTaken || !cardHasMovement(currentCard()) ? "disabled" : ""}>次要移动</button>
    <button id="fastMoveBtn" ${!active || me.actionTaken || !cardHasMovement(currentCard()) ? "disabled" : ""}>快速移动</button>
    <button id="skipBtn" ${!active || me.actionTaken ? "disabled" : ""}>放弃效果</button>
    <button id="finishBtn" class="primary" ${!active ? "disabled" : ""}>确认结束</button>
  `;
  $("mainBtn")?.addEventListener("click", () => {
    selectedMain = true;
    selectedMove = false;
    selectedFastMove = false;
    legalCells = legalMainCells(me);
    setHint("点击绿色闪烁格执行主要动作。");
    renderBoard();
  });
  $("moveBtn")?.addEventListener("click", () => {
    selectedMove = true;
    selectedFastMove = false;
    selectedMain = false;
    legalCells = legalMoveCells(me);
    setHint("点击绿色闪烁格移动。");
    renderBoard();
  });
  $("fastMoveBtn")?.addEventListener("click", () => {
    selectedFastMove = true;
    selectedMove = false;
    selectedMain = false;
    legalCells = legalFastMoveCells(me);
    setHint("点击绿色闪烁格执行快速移动。");
    renderBoard();
  });
  $("skipBtn")?.addEventListener("click", async () => {
    state = await request("/api/skip-action");
    selectedMain = false;
    selectedMove = false;
    selectedFastMove = false;
    legalCells.clear();
    render();
  });
  $("finishBtn")?.addEventListener("click", async () => {
    state = await request("/api/finish");
    selectedMain = false;
    selectedMove = false;
    selectedFastMove = false;
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
    const status = cardRoundStatus(me, c);
    const disabled = state.phase !== "planning" || c.displayOnly || Boolean(status);
    const label = c.displayOnly ? "占位" : status || "选择";
    return cardHtml(c, { used: Boolean(status), button: `<button data-select="${c.id}" ${disabled ? "disabled" : ""}>${label}</button>` });
  }).join("");
  document.querySelectorAll("[data-select]").forEach((btn) => btn.addEventListener("click", selectCard));
}

function cardHtml(card, options = {}) {
  const cls = ["card", colorClass(card.color)];
  if (options.compact) cls.push("compact-card");
  if (options.used) cls.push("used-card");
  if (options.resolved) cls.push("resolved-card");
  const owner = options.owner ? `<span class="card-owner">${escapeHtml(options.owner)}</span>` : "";
  const subtype = cardSubtypeText(card);
  const passive = options.upgradePassive ? `<footer>获得被动：${escapeHtml(options.upgradePassive)}</footer>` : "";
  return `
    <article class="${cls.join(" ")}">
      <header class="card-head">
        <strong>${highlightKeywords(card.name)}</strong>
        <span>先攻${statText(card, "initiative")} · ${cardTypeText(card)}</span>
      </header>
      ${owner}
      <div class="stats">
        ${stat("防", card, "defense")}
        ${stat("移", card, "movement")}
        ${stat("攻", card, "attack")}
        ${subtype ? `<span>${subtype}</span>` : ""}
      </div>
      <p>${highlightKeywords(card.text)}</p>
      ${passive}
      ${options.button || ""}
    </article>
  `;
}

function cardRoundStatus(player, card) {
  if (player.roundUsed?.includes(card.id)) return `回合${state.turn}已打出`;
  if (player.played === card.id) return `回合${state.turn}已打出`;
  if (player.discard?.includes(card.id)) return "已弃置";
  if (player.selectedCardId === card.id) return "已暗选";
  return "";
}

function cardTypeText(card) {
  if (card.primaryCategory) return card.primaryCategory;
  const map = {
    attackGeneric: "攻击",
    attackRanged: "攻击",
    attackArea: "攻击",
    attackHero: "攻击",
    attackAny: "攻击",
    killMinion: "攻击",
    skillGeneric: "技能",
    effectGeneric: "技能",
    defenseGeneric: "防御",
    primaryMove: "移动",
  };
  return map[card.primary] || "技能";
}

function cardSubtypeText(card) {
  const subtype = card.subtype;
  if (!subtype?.type) return "";
  const field = subtype.type === "范围" ? "range" : subtype.type === "远程" ? "ranged" : null;
  const base = subtype.value ?? 0;
  const bonus = field ? (card.bonusStats?.[field] || 0) : 0;
  return `${subtype.type}${bonus ? `${base}+${bonus}` : base}`;
}

function upgradeCandidateCards(player) {
  return availableUpgradeColors(player).flatMap((color) => {
    const next = player.skills[color] + 1;
    const candidates = allHeroCards(player).filter((card) => colorKey(card.color) === color && card.level === next);
    return candidates.map((card) => {
      const other = candidates.find((c) => c.id !== card.id);
      const passive = other?.passiveBonus ? `${other.passiveBonus}+1` : "无";
      return { card, passive };
    });
  });
}

function renderLog() {
  $("log").innerHTML = state.log.map((line) => `<div>${escapeHtml(line)}</div>`).join("");
}

function legalMoveCells(me) {
  const c = currentCard();
  if (!cardHasMovement(c) || !me.pos) return new Set();
  const occupied = new Set([
    ...state.players.filter((p) => p.pos && !p.defeated).map((p) => key(p.pos)),
    ...state.minions.map((m) => key(m)),
  ]);
  return new Set(state.map
    .filter((cell) => !cell.obstacle && !occupied.has(key(cell)))
    .filter((cell) => canFastTravel(me, cell) || walkDistance(me.pos, cell, occupied, Boolean(c.canPhaseThroughWalls)) <= c.movement)
    .map(key));
}

function legalFastMoveCells(me) {
  const c = currentCard();
  if (!cardHasMovement(c) || !me.pos) return new Set();
  const occupied = occupiedKeys();
  return new Set(state.map
    .filter((cell) => !cell.obstacle && !occupied.has(key(cell)))
    .filter((cell) => canFastTravel(me, cell))
    .map(key));
}

function legalMainCells(me) {
  const c = currentCard();
  if (!c || !me.pos) return new Set();
  if (c.primary === "killMinion") {
    return new Set(state.minions
      .filter((m) => m.team !== me.team && hexDistance(me.pos, m) === 1)
      .filter((m) => !immuneTo(m, "attack"))
      .filter((m) => m.kind !== "heavy" || !state.minions.some((other) => other.team === m.team && other.kind !== "heavy"))
      .map(key));
  }
  if (c.primary === "attackHero") {
    return new Set(state.players
      .filter((p) => p.team !== me.team && p.pos && !p.defeated && hexDistance(me.pos, p.pos) === 1)
      .filter((p) => !immuneTo(p, "attack"))
      .map((p) => key(p.pos)));
  }
  if (c.primary === "attackAny") {
    return new Set([
      ...state.minions.filter((m) => m.team !== me.team && !immuneTo(m, "attack")).map(key),
      ...state.players.filter((p) => p.team !== me.team && p.pos && !p.defeated && !immuneTo(p, "attack")).map((p) => key(p.pos)),
    ]);
  }
  if (["attackGeneric", "attackRanged", "attackArea"].includes(c.primary)) {
    const range = cardRange(c);
    return new Set([
      ...state.minions.filter((m) => m.team !== me.team && !immuneTo(m, "attack") && hexDistance(me.pos, m) <= range).map(key),
      ...state.players.filter((p) => p.team !== me.team && p.pos && !p.defeated && !immuneTo(p, "attack") && hexDistance(me.pos, p.pos) <= range).map((p) => key(p.pos)),
    ]);
  }
  if (["arien-07-潮水", "arien-09-魔法水流", "arien-11-潮汐之力"].includes(c.id)) {
    const range = cardRange(c);
    const occupied = occupiedKeys();
    return new Set(state.map
      .filter((cell) => !cell.obstacle && !occupied.has(key(cell)) && hexDistance(me.pos, cell) <= range)
      .filter((cell) => !String(cell.state || "").endsWith("HeroSpawn"))
      .filter((cell) => c.id === "arien-11-潮汐之力" || !state.map.some((spawn) => String(spawn.state || "").endsWith("HeroSpawn") && !occupied.has(key(spawn)) && hexDistance(cell, spawn) === 1))
      .map(key));
  }
  if (["wasp-09-意念操控", "wasp-11-心灵控制"].includes(c.id)) {
    const range = cardRange(c);
    return new Set([
      ...state.minions.filter((m) => hexDistance(me.pos, m) <= range && !isAligned(me.pos, m)).map(key),
      ...state.players.filter((p) => p.pos && !p.defeated && p.seat !== me.seat && hexDistance(me.pos, p.pos) <= range && !isAligned(me.pos, p.pos)).map((p) => key(p.pos)),
    ]);
  }
  if (["wasp-13-控物", "wasp-15-引力控制", "wasp-17-意念黑洞"].includes(c.id)) {
    const range = cardRange(c);
    return new Set([
      ...state.minions.filter((m) => hexDistance(me.pos, m) <= range && hexDistance(me.pos, m) > 1).map(key),
      ...state.players.filter((p) => p.pos && !p.defeated && p.seat !== me.seat && hexDistance(me.pos, p.pos) <= range && hexDistance(me.pos, p.pos) > 1).map((p) => key(p.pos)),
    ]);
  }
  if (["skillGeneric", "defenseGeneric", "effectGeneric"].includes(c.primary)) {
    return new Set([key(me.pos)]);
  }
  if (c.primary === "primaryMove") {
    const range = cardRange(c);
    const occupied = occupiedKeys();
    return new Set(state.map
      .filter((cell) => !cell.obstacle && !occupied.has(key(cell)))
      .filter((cell) => walkDistance(me.pos, cell, occupied, Boolean(c.canPhaseThroughWalls)) <= range)
      .map(key));
  }
  return new Set();
}

function cardHasMovement(card) {
  return !!card && (card.movement !== null && card.movement !== undefined || card.primary === "move" || card.primary === "primaryMove");
}

function cardRange(card) {
  const type = card?.subtype?.type;
  const bonusKey = type === "范围" ? "range" : type === "远程" ? "ranged" : null;
  return (card?.subtype?.value ?? card?.movement ?? 1) + (bonusKey ? (card?.bonusStats?.[bonusKey] || 0) : 0);
}

function isAligned(a, b) {
  return a.x === b.x || a.y === b.y || (a.x + a.y) === (b.x + b.y);
}

function occupiedKeys() {
  return new Set([
    ...state.players.filter((p) => p.pos && !p.defeated).map((p) => key(p.pos)),
    ...state.minions.map((m) => key(m)),
  ]);
}

function normalizedAction(action) {
  return {
    basicAttack: "attack",
    "基础攻击": "attack",
    "攻击": "attack",
    basicSkill: "skill",
    "基础技能": "skill",
    "技能": "skill",
  }[action] || action;
}

function immuneTo(piece, action) {
  const immunities = piece?.immunities || piece?.immune || [];
  const list = Array.isArray(immunities) ? immunities : [immunities];
  const normalized = new Set(list.map(normalizedAction));
  return normalized.has("all") || normalized.has("全部") || normalized.has(normalizedAction(action));
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

async function selectHero(event) {
  try {
    state = await request("/api/select-hero", { heroKey: event.currentTarget.value });
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
    const [color, cardId] = event.currentTarget.dataset.upgrade.split(":");
    state = await request("/api/upgrade", { color, cardId });
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
  if (b.initiative) parts.push(`先攻+${b.initiative}`);
  if (b.movement) parts.push(`移动+${b.movement}`);
  if (b.range) parts.push(`范围+${b.range}`);
  if (b.ranged) parts.push(`远程+${b.ranged}`);
  if (!parts.length) return ["无被动", "", ""];
  return [parts.slice(0, 2).join("、"), parts.slice(2, 4).join("、"), parts.slice(4).join("、")];
}

function upgradeButtons(player) {
  const colors = availableUpgradeColors(player);
  if (!colors.length && !player.hasUltimate) return `<span class="muted">已满足大招条件，等待系统授予。</span>`;
  const names = { red: "红", green: "绿", blue: "蓝" };
  return `
    <span class="muted">选择一张下一等级卡替换当前同色卡；未选的另一张提供被动。</span>
    ${colors.map((color) => {
      const next = player.skills[color] + 1;
      const candidates = allHeroCards(player).filter((card) => colorKey(card.color) === color && card.level === next);
      if (!candidates.length) return `<span class="muted">${names[color]}卡缺少 ${next} 级升级选项。</span>`;
      return candidates.map((card) => {
        const other = candidates.find((c) => c.id !== card.id);
        const passive = other?.passiveBonus ? `${other.passiveBonus}+1` : "无";
        return `<button data-upgrade="${color}:${card.id}">${names[color]}升${next}：${highlightKeywords(card.name)}（未选被动：${passive}）</button>`;
      }).join("");
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
function colorKey(color) { return { 红: "red", 绿: "green", 蓝: "blue" }[color] || ""; }
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
    "Hero selection only in lobby": "只能在大厅选择英雄",
    "Unknown hero": "未知英雄",
    "Need target in range": "目标不在范围内",
    "Target is immune": "目标免疫该行动",
    "Card has no movement action": "这张牌没有移动行动",
  }[value] || value || "请求失败";
}
function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#039;" }[ch]));
}

const keywordGroups = [
  { cls: "kw-timing", words: ["攻击后", "攻击前", "攻击结束后", "防御结束后", "下一回合", "本回合", "本轮", "此回合"] },
  { cls: "kw-active", words: ["激活", "可以", "选择", "取回", "丢弃", "弃置", "交换", "推动"] },
  { cls: "kw-action", words: ["基础攻击", "攻击", "基础技能", "技能", "终极技能", "防御/技能", "防御", "移动"] },
  { cls: "kw-range", words: ["远程", "范围", "相邻", "战斗区域"] },
  { cls: "kw-immune", words: ["免疫"] },
];

function highlightKeywords(value) {
  let text = escapeHtml(value);
  for (const group of keywordGroups) {
    const words = [...group.words].sort((a, b) => b.length - a.length).map(escapeRegExp).join("|");
    text = text.replace(new RegExp(words, "g"), (match) => `<span class="${group.cls}">${match}</span>`);
  }
  return text;
}

function escapeRegExp(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

setInterval(refresh, 1000);
refresh();
