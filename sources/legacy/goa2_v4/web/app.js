"use strict";

const IDENTITY_STORAGE_KEY = "goa2_v4.identity.v3";
const DEFAULT_ROOM_ID = "local-v4-main";
const POLL_INTERVAL_MS = 1000;

const model = {
  catalog: null,
  cards: [],
  heroes: [],
  map: [],
  payload: null,
  catalogFilter: "all",
  pendingCommand: false,
  debugTeleport: false,
  debugRemoveMinion: false,
  boardScale: 1,
  roomId: DEFAULT_ROOM_ID,
  seat: 0,
  token: "",
  tokens: {},
  setupDirty: false,
  pollTimer: null,
  pollInFlight: false,
};

const $ = (id) => document.getElementById(id);

function normalizeSeat(value, fallback = 0) {
  const seat = Number(value);
  return Number.isInteger(seat) && seat >= 0 && seat <= 3 ? seat : fallback;
}

function loadIdentity() {
  let stored = {};
  try {
    const persisted = localStorage.getItem(IDENTITY_STORAGE_KEY);
    const legacy = sessionStorage.getItem(IDENTITY_STORAGE_KEY);
    stored = JSON.parse(persisted || legacy || "{}");
    if (!persisted && legacy) {
      localStorage.setItem(IDENTITY_STORAGE_KEY, legacy);
      sessionStorage.removeItem(IDENTITY_STORAGE_KEY);
    }
  } catch {
    stored = {};
  }
  const params = new URLSearchParams(window.location.search);
  model.roomId = cleanText(
    params.get("room"),
    cleanText(stored.roomId, DEFAULT_ROOM_ID),
  );
  model.seat = normalizeSeat(params.get("seat"), normalizeSeat(stored.seat));
  model.tokens = stored.roomId === model.roomId && stored.tokens && typeof stored.tokens === "object"
    ? { ...stored.tokens }
    : {};
  if (
    stored.roomId === model.roomId
    && normalizeSeat(stored.seat) === model.seat
    && cleanText(stored.token, "")
  ) {
    model.tokens[model.seat] = cleanText(stored.token, "");
  }
  model.token = cleanText(model.tokens[model.seat], "");
}

function persistIdentity() {
  localStorage.setItem(IDENTITY_STORAGE_KEY, JSON.stringify({
    roomId: model.roomId,
    seat: model.seat,
    token: model.token,
    tokens: model.tokens,
  }));
}

function updateIdentityUrl() {
  const url = new URL(window.location.href);
  url.searchParams.set("room", model.roomId);
  url.searchParams.set("seat", String(model.seat));
  window.history.replaceState(null, "", url);
}

function stateUrl() {
  const query = new URLSearchParams({
    room_id: model.roomId,
    token: model.token,
  });
  return `/api/state?${query.toString()}`;
}

function commandBody(path, body = {}) {
  const identified = {
    ...body,
    room_id: model.roomId,
  };
  if (path.startsWith("/api/debug/") && !model.token) return identified;
  return {
    ...identified,
    token: model.token,
  };
}

const phaseLabels = {
  setup: "整备阶段",
  planning: "计划阶段",
  card_selection: "计划阶段",
  reveal: "公开行动",
  card_reveal: "公开行动",
  action: "行动阶段",
  card_resolution: "行动阶段",
  cleanup: "轮末阶段",
  round_end: "轮末阶段",
  upgrade: "升级阶段",
  finished: "对局结束",
  game_over: "对局结束",
};

const eventLabels = {
  GameReset: "对局已重置",
  ControlledSeatChanged: "控制席位已切换",
  CardSelected: "已选择行动牌",
  SelectionConfirmed: "已确认行动牌",
  CardsRevealed: "公开行动牌",
  action_skipped: "跳过行动",
  HeroMoved: "英雄移动",
  AttackDeclared: "发起攻击",
  DefenseResolved: "完成防御结算",
  PhaseChanged: "阶段推进",
};

const garbledPattern = /(?:锛|銆|鈥|鏈|瑁呮|娴嬭瘯|闂|绉诲姩|鏀诲嚮|闃插尽|鎶€鑳|鐗岃|鑻遍泟)/;

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function firstDefined(...values) {
  return values.find((value) => value !== undefined && value !== null);
}

function arrayOf(value) {
  if (Array.isArray(value)) return value;
  if (value && typeof value === "object") return Object.values(value);
  return [];
}

function cleanText(value, fallback) {
  if (typeof value !== "string" || !value.trim() || garbledPattern.test(value)) {
    return fallback;
  }
  return value.trim();
}

loadIdentity();

function apiCardName(card, index = 0) {
  const heroId = firstDefined(card?.hero_id, card?.heroId, "card");
  return cleanText(card?.name, `${String(heroId).toUpperCase()} 卡牌 ${index + 1}`);
}

function heroName(hero, index = 0) {
  const id = firstDefined(hero?.hero_id, hero?.heroId, hero?.id, `hero-${index + 1}`);
  return cleanText(hero?.name, `英雄 ${String(id).toUpperCase()}`);
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: { "content-type": "application/json", ...(options.headers || {}) },
    ...options,
  });
  let data = null;
  try {
    data = await response.json();
  } catch {
    data = {};
  }
  if (!response.ok) {
    const errorValue = firstDefined(data?.error?.message, data?.error, data?.detail);
    const error = new Error(cleanText(errorValue, `请求失败 (${response.status})`));
    error.status = response.status;
    error.payload = data;
    throw error;
  }
  return data;
}

function post(path, body = {}) {
  return api(path, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

function stateRoot() {
  return model.payload?.state || model.payload?.game || model.payload || {};
}

function privateView() {
  const state = stateRoot();
  return firstDefined(
    model.payload?.private_view,
    model.payload?.privateView,
    state.private_view,
    state.privateView,
    model.payload?.private,
    state.private,
    {},
  );
}

function phase() {
  const state = stateRoot();
  return String(firstDefined(state.phase, state.stage, "setup"));
}

function controlledSeatIndex() {
  const state = stateRoot();
  return Number(firstDefined(
    state.controlled_seat,
    state.controlledSeat,
    model.payload?.controlled_seat,
    model.payload?.controlledSeat,
    model.seat,
  ));
}

function seats() {
  const state = stateRoot();
  return arrayOf(firstDefined(model.payload?.seats, state.seats, state.players, model.payload?.players));
}

function controlledSeat() {
  const index = controlledSeatIndex();
  return seats().find((seat, seatIndex) => {
    const seatId = Number(firstDefined(seat.seat, seat.seat_id, seat.id, seatIndex));
    return seatId === index;
  }) || seats()[index] || null;
}

function cardId(card) {
  if (typeof card === "string") return card;
  return firstDefined(card?.id, card?.card_id, card?.cardId);
}

function hydrateCard(card, index = 0) {
  if (typeof card !== "string") return card || {};
  return model.cards.find((candidate) => cardId(candidate) === card) || {
    id: card,
    name: `卡牌 ${index + 1}`,
    implementation_status: "data_only",
  };
}

function catalogHeroes(payload) {
  return arrayOf(firstDefined(payload?.heroes, payload?.catalog?.heroes));
}

function catalogCards(payload, heroes) {
  const direct = arrayOf(firstDefined(payload?.cards, payload?.catalog?.cards));
  if (direct.length) return direct;
  return heroes.flatMap((hero) => arrayOf(hero.cards));
}

function mapCells() {
  const state = stateRoot();
  return arrayOf(firstDefined(
    model.catalog?.map?.cells,
    model.catalog?.map,
    model.catalog?.cells,
    model.catalog?.board?.cells,
    model.payload?.map?.cells,
    model.payload?.map,
    state.map?.cells,
    state.map,
    state.board?.cells,
    state.board,
  ));
}

function seatTeam(seat, index = 0) {
  const raw = firstDefined(seat?.team, seat?.team_id, seat?.teamId, index % 2 === 0 ? "blue" : "red");
  if (raw === 0 || raw === "0" || raw === "blue" || raw === "team_a" || raw === "A") return "blue";
  return "red";
}

function seatHero(seat) {
  if (!seat) return {};
  if (seat.hero && typeof seat.hero === "object") return seat.hero;
  const heroId = firstDefined(seat.hero_id, seat.heroId, seat.hero);
  return model.heroes.find((hero) => firstDefined(hero.hero_id, hero.heroId, hero.id) === heroId) || {
    id: heroId,
  };
}

function currentHand() {
  const seat = controlledSeat();
  const privateState = privateView();
  const cards = arrayOf(firstDefined(
    seat?.initial_hand,
    seat?.initialHand,
    seat?.hand,
    privateState.initial_hand,
    privateState.initialHand,
    privateState.hand,
  ));
  return cards.map(hydrateCard);
}

function selectedCardId() {
  const seat = controlledSeat();
  const privateState = privateView();
  return firstDefined(
    privateState.selected_card_id,
    privateState.selectedCardId,
    seat?.selected_card_id,
    seat?.selectedCardId,
  );
}

function controlledSeatConfirmed() {
  const seat = controlledSeat();
  const privateState = privateView();
  return Boolean(firstDefined(
    privateState.confirmed,
    privateState.selection_confirmed,
    privateState.selectionConfirmed,
    seat?.confirmed,
    seat?.selection_confirmed,
    seat?.selectionConfirmed,
    false,
  ));
}

function allSeatsConfirmed() {
  const seatList = seats();
  return seatList.length === 4 && seatList.every((seat) => Boolean(firstDefined(
    seat.confirmed,
    seat.selection_confirmed,
    seat.selectionConfirmed,
    false,
  )));
}

function hasPrivateAction(action) {
  return arrayOf(privateView().legal_actions).includes(action);
}

function hasPublicAction(action) {
  return arrayOf(stateRoot().legal_actions).includes(action);
}

function hasDebugAction(action) {
  return arrayOf(stateRoot().debug_actions).includes(action);
}

function revision() {
  const state = stateRoot();
  return firstDefined(state.revision, model.payload?.revision);
}

function debugBody(extra = {}) {
  const value = revision();
  return value === undefined ? extra : {
    ...extra,
    expected_revision: value,
    expectedRevision: value,
  };
}

function statValue(card, type) {
  const secondary = firstDefined(card?.secondary_actions, card?.secondaryActions, {});
  const action = secondary[type] || {};
  return firstDefined(
    card?.[type],
    action.value,
    action.has_action === false || action.hasAction === false ? null : undefined,
  );
}

function primaryAction(card) {
  return firstDefined(card?.primary_action, card?.primaryAction, {});
}

function primarySubtype(card) {
  return firstDefined(primaryAction(card).subtype, {});
}

function rangeStat(card) {
  const subtype = primarySubtype(card);
  return {
    label: cleanText(subtype.type, "范围/远程"),
    value: firstDefined(subtype.value, "—"),
  };
}

function cardStatus() {
  return "未实装未测试";
}

function cardMarkup(card, index = 0, selectable = false) {
  const action = primaryAction(card);
  const color = firstDefined(card?.color_key, card?.colorKey, "silver");
  const level = card?.level === null || card?.level === undefined ? "基础" : `Lv.${card.level}`;
  const category = cleanText(action.category, cleanText(action.family, "行动"));
  const text = cleanText(action.text, "卡牌规则文本由正式目录提供。");
  const range = rangeStat(card);
  const id = cardId(card);
  const selected = selectable && id === selectedCardId();
  const disabled = !selectable || !hasPrivateAction("select_card") || model.pendingCommand;
  const openingTag = selectable
    ? `<button type="button" class="card color-${escapeHtml(color)} ${selected ? "selected" : ""} selectable" data-card-id="${escapeHtml(id)}" aria-pressed="${selected}" ${disabled ? "disabled" : ""}>`
    : `<article class="card color-${escapeHtml(color)}">`;
  const closingTag = selectable ? "</button>" : "</article>";
  return `
    ${openingTag}
      <div class="card-top">
        <span>${escapeHtml(level)} · 先攻 ${escapeHtml(firstDefined(card?.initiative, "—"))}</span>
        <span class="status-badge">${cardStatus()}</span>
      </div>
      <h3 title="${escapeHtml(apiCardName(card, index))}">${escapeHtml(apiCardName(card, index))}</h3>
      <p class="card-kind">${escapeHtml(category)}</p>
      <div class="card-stats">
        <span>主要行动<strong>${escapeHtml(firstDefined(action.value, "—"))}</strong></span>
        <span>次要移动<strong>${escapeHtml(firstDefined(statValue(card, "movement"), "—"))}</strong></span>
        <span>次要防御<strong>${escapeHtml(firstDefined(statValue(card, "defense"), "—"))}</strong></span>
        <span>${escapeHtml(range.label)}<strong>${escapeHtml(range.value)}</strong></span>
      </div>
      <p class="card-text">${escapeHtml(text)}</p>
    ${closingTag}
  `;
}

function renderConnection(status, label) {
  $("connectionDot").className = `connection-dot ${status}`;
  $("connectionLabel").textContent = label;
}

function renderRoom() {
  const state = stateRoot();
  const roomConfig = state.room_config || {};
  $("roomCode").textContent = cleanText(
    firstDefined(state.room_code, state.roomCode, model.payload?.room_code, model.payload?.roomCode),
    cleanText(model.payload?.room_id, model.roomId),
  );
  $("identitySeat").textContent = `席位 ${model.seat + 1}`;
  $("revision").textContent = `revision ${firstDefined(revision(), "—")}`;
  $("seatSelect").value = String(model.seat);
  $("seatSelect").disabled = model.pendingCommand;
  if (!model.setupDirty) {
    $("setupCrystalLife").value = firstDefined(roomConfig.starting_crystal_life, 7);
    $("setupFrontlineMarks").value = firstDefined(roomConfig.frontline_victory_marks, 3);
    const selectedHeroes = arrayOf(roomConfig.hero_ids);
    [0, 1, 2, 3].forEach((seat) => {
      const select = $(`setupHero${seat}`);
      select.innerHTML = model.heroes.map((hero) => {
        const heroId = cleanText(firstDefined(hero.hero_id, hero.id), "");
        const selected = selectedHeroes[seat] === heroId ? "selected" : "";
        return `<option value="${escapeHtml(heroId)}" ${selected}>${escapeHtml(heroName(hero, seat))}</option>`;
      }).join("");
    });
  }
  $("configureRoomButton").disabled = !model.payload || !roomConfig.configurable || model.pendingCommand;
  $("resetButton").disabled = !model.payload;
  $("forceConfirmButton").disabled = !model.payload || !hasDebugAction("force_confirm") || model.pendingCommand;
  $("teleportButton").disabled = !model.payload || !hasDebugAction("debug_teleport") || model.pendingCommand;
  $("teleportButton").textContent = `调试传送：${model.debugTeleport ? "开启" : "关闭"}`;
  $("debugSkipCurrentButton").disabled = !model.payload || !hasDebugAction("skip_current") || model.pendingCommand;
  $("debugSkipAllButton").disabled = !model.payload || !hasDebugAction("skip_all") || model.pendingCommand;
  $("debugCoinsButton").disabled = !model.payload || !hasDebugAction("set_coins") || model.pendingCommand;
  $("debugCrystalButton").disabled = !model.payload || !hasDebugAction("set_crystal") || model.pendingCommand;
  $("debugFrontlineButton").disabled = !model.payload || !hasDebugAction("set_frontline") || model.pendingCommand;
  $("debugDefeatButton").disabled = !model.payload || !hasDebugAction("defeat_hero") || model.pendingCommand;
  $("debugResetMinionsButton").disabled = !model.payload || !hasDebugAction("reset_minions") || model.pendingCommand;
  $("debugAdvanceBlueButton").disabled = !model.payload || !hasDebugAction("advance_frontline") || model.pendingCommand;
  $("debugAdvanceRedButton").disabled = !model.payload || !hasDebugAction("advance_frontline") || model.pendingCommand;
  $("enterRoundEndButton").disabled = !model.payload || !hasDebugAction("enter_round_end") || model.pendingCommand;
  $("removeMinionButton").disabled = !model.payload || !hasDebugAction("remove_minion") || model.pendingCommand;
  $("removeMinionButton").textContent = `调试移除小兵：${model.debugRemoveMinion ? "开启" : "关闭"}`;
}

function renderPlayers() {
  const current = controlledSeatIndex();
  const activeSeat = Number(firstDefined(
    stateRoot().active_seat,
    stateRoot().activeSeat,
    stateRoot().turn_seat,
    stateRoot().turnSeat,
    -1,
  ));
  const playerList = seats();
  const teamStates = arrayOf(stateRoot().teams);
  $("players").innerHTML = [0, 1, 2, 3].map((slot) => {
    const seat = playerList.find((item, index) => Number(firstDefined(item.seat, item.seat_id, item.id, index)) === slot);
    if (!seat) {
      return `
        <article class="player-card ${slot % 2 ? "team-red" : ""}">
          <div class="player-avatar">P${slot + 1}</div>
          <div class="player-main">
            <h3>席位 ${slot + 1}</h3>
            <p>等待服务器状态</p>
          </div>
          <div class="player-stats"><span>未就绪</span></div>
        </article>
      `;
    }
    const hero = seatHero(seat);
    const team = seatTeam(seat, slot);
    const heroLabel = heroName(hero, slot);
    const selected = Boolean(firstDefined(seat.selected, seat.has_selected, seat.hasSelected, false));
    const confirmed = Boolean(firstDefined(seat.confirmed, seat.selection_confirmed, seat.selectionConfirmed, false));
    const defeated = Boolean(firstDefined(seat.defeated, seat.is_defeated, seat.isDefeated, false));
    const coins = firstDefined(seat.coins, seat.gold, "—");
    const handSize = firstDefined(seat.hand_size, seat.handSize, arrayOf(seat.hand).length || "—");
    const teamState = teamStates.find((item) => seatTeam(item) === team) || {};
    const crystalLife = firstDefined(teamState.crystal_life, teamState.crystalLife, "—");
    const frontlineMarks = firstDefined(teamState.frontline_marks, teamState.frontlineMarks, 0);
    const captainSeat = Number(firstDefined(teamState.captain_seat, teamState.captainSeat, -1));
    return `
      <article class="player-card team-${team} ${slot === current ? "controlled" : ""} ${slot === activeSeat ? "active" : ""}">
        <div class="player-avatar">${escapeHtml(heroLabel.slice(0, 2))}</div>
        <div class="player-main">
          <h3>${escapeHtml(heroLabel)}</h3>
          <p>席位 ${slot + 1} · ${team === "blue" ? "蓝队" : "红队"} · ${defeated ? "已击败" : confirmed ? "已确认" : selected ? "已选牌" : "待选牌"}</p>
        </div>
        <div class="player-stats">
          <span>金币 ${escapeHtml(coins)}</span>
          <span>手牌 ${escapeHtml(handSize)}</span>
          <span>水晶 ${escapeHtml(crystalLife)}</span>
          <span>战线标记 ${escapeHtml(frontlineMarks)}</span>
          <span>${captainSeat === slot ? "队长" : "队员"}</span>
          <span>${slot === current ? "当前控制" : "公开视图"}</span>
        </div>
      </article>
    `;
  }).join("");
}

function initiativeEntries() {
  const state = stateRoot();
  if (!["card_resolution", "round_end"].includes(phase())) return [];
  const revealed = arrayOf(firstDefined(
    state.revealed_cards,
    state.revealedCards,
    state.public_cards,
    state.publicCards,
  ));
  const revealedBySeat = new Map();
  revealed.forEach((entry, index) => {
    const seat = Number(firstDefined(entry?.seat, entry?.seat_id, entry?.seatId, index));
    revealedBySeat.set(seat, entry);
  });
  return arrayOf(firstDefined(state.initiative_order, state.initiativeOrder)).map((entry, index) => {
    const seat = Number(typeof entry === "number" ? entry : firstDefined(entry?.seat, entry?.seat_id, entry?.seatId, index));
    const revealedEntry = revealedBySeat.get(seat);
    const id = typeof entry === "string" ? entry : firstDefined(
      entry?.card_id,
      entry?.cardId,
      entry?.revealed_card_id,
      revealedEntry?.card_id,
      revealedEntry?.cardId,
      revealedEntry?.revealed_card_id,
      revealedEntry,
      seats()[seat]?.revealed_card_id,
      seats()[seat]?.revealedCardId,
    );
    const card = hydrateCard(id, index);
    return {
      ...card,
      effective_initiative: firstDefined(revealedEntry?.initiative, card.initiative),
      seat,
    };
  }).filter((entry) => cardId(entry));
}

function renderActions() {
  const state = stateRoot();
  const currentPhase = phase();
  const round = firstDefined(state.round, state.round_number, state.roundNumber, "—");
  const pendingCaptain = state.pending_captain_choice;
  const pendingAttack = state.pending_attack;
  const winner = state.winner;
  $("phaseTitle").textContent = phaseLabels[currentPhase] || cleanText(currentPhase, "等待状态");
  $("roundLabel").textContent = `第 ${round} 轮`;
  $("statusLine").textContent = winner
    ? `${winner === "blue" ? "蓝队" : "红队"}获胜`
    : pendingAttack
      ? `Attack pending: ${pendingAttack.card_id} -> ${pendingAttack.target_id} (${pendingAttack.attack_value})`
    : pendingCaptain
      ? `等待席位 ${Number(pendingCaptain.chooser) + 1} 队长执行${
        pendingCaptain.kind === "remove_minions" ? "小兵移除" : "小兵出生落点"
      }`
      : currentPhase === "round_end"
        ? `当前小兵战斗区域：${cleanText(state.combat_region, "未设置")}`
        : cleanText(
          firstDefined(state.status_text, state.statusText, state.prompt, model.payload?.prompt),
          "公开信息与可用操作均来自服务器。",
        );

  const activeSeat = Number(firstDefined(state.active_seat, state.activeSeat, -1));
  const resolvedSeats = new Set(
    arrayOf(firstDefined(state.resolved_seats, state.resolvedSeats)).map(Number),
  );
  const revealed = initiativeEntries();
  $("publicActions").innerHTML = revealed.length ? revealed.map((card, index) => {
    const action = primaryAction(card);
    const color = firstDefined(card.color_key, card.colorKey, "silver");
    return `
      <article class="public-card color-${escapeHtml(color)} ${card.seat === activeSeat ? "active" : ""}">
        <span class="initiative-rank">#${index + 1} · 席位 ${card.seat + 1}</span>
        <span class="status-badge">${resolvedSeats.has(card.seat) ? "已结算" : cardStatus()}</span>
        <h3>${escapeHtml(apiCardName(card, index))}</h3>
        <p>${escapeHtml(cleanText(action.category, "公开行动"))} · 先攻 ${escapeHtml(firstDefined(card.initiative, "—"))}</p>
      </article>
    `;
  }).join("") : `<div class="empty-message compact">${
    currentPhase === "card_reveal" ? "四席已确认，正在自动公开" : "行动牌尚未公开"
  }</div>`;

  const canSkip = hasPrivateAction("skip_action");
  const canChoose = hasPrivateAction("choose_action");
  const canComplete = hasPrivateAction("complete_action");
  const canBeginRoundEnd = hasPublicAction("begin_round_end") || hasDebugAction("begin_round_end");
  const actionOptions = arrayOf(privateView().action_options);
  const optionLabels = {
    primary: "主要行动",
    secondary_movement: "次要移动",
  };
  $("serverActions").innerHTML = `
    <button id="skipActionButton" class="server-action ${canSkip ? "enabled" : ""}" type="button" ${canSkip && !model.pendingCommand ? "" : "disabled"}>
      ${canSkip ? "跳过当前行动" : currentPhase === "card_resolution" ? "请切换到当前行动席位" : "暂无行动"}
    </button>
    <button id="completeActionButton" class="server-action ${canComplete ? "enabled" : ""}" type="button"
      ${canComplete && !model.pendingCommand ? "" : "disabled"}>
      ${canComplete ? "完成未实装主要行动（仅占位）" : "尚无待完成的未实装行动"}
    </button>
    ${actionOptions.map((option, index) => `
      <button class="server-action ${canChoose ? "enabled" : ""}" type="button"
        data-action-option="${index}" ${canChoose && !model.pendingCommand ? "" : "disabled"}>
        ${option.kind === "fast_move"
          ? `快速移动（替代${option.source_slot === "primary" ? "主要移动" : "次要移动"}）`
          : optionLabels[option.kind] || option.kind}
      </button>
    `).join("")}
    <button id="beginRoundEndButton" class="server-action ${canBeginRoundEnd ? "enabled" : ""}" type="button"
      ${canBeginRoundEnd && !model.pendingCommand ? "" : "disabled"}>
      ${canBeginRoundEnd ? "调试：开始轮末结算" : "轮末结算未就绪"}
    </button>
  `;
  $("skipActionButton").addEventListener("click", () => runCommand("/api/actions/skip", {
    seat: controlledSeatIndex(),
  }));
  $("completeActionButton").addEventListener("click", () => runCommand("/api/actions/complete", {
    seat: controlledSeatIndex(),
  }));
  document.querySelectorAll("[data-action-option]").forEach((button) => {
    button.addEventListener("click", () => {
      const option = actionOptions[Number(button.dataset.actionOption)];
      runCommand("/api/actions/choose", {
        seat: controlledSeatIndex(),
        kind: option.kind,
        source_slot: option.source_slot,
      });
    });
  });
  $("beginRoundEndButton").addEventListener("click", () => {
    runDebug("/api/debug/begin-round-end");
  });
}

function unitPosition(unit) {
  const position = firstDefined(unit?.position, unit?.cell, unit?.coordinate, {});
  return {
    x: Number(firstDefined(position.q, position.x, unit?.q, unit?.x)),
    y: Number(firstDefined(position.r, position.y, unit?.r, unit?.y)),
  };
}

function unitsByPosition() {
  const state = stateRoot();
  const unitList = arrayOf(firstDefined(state.units, model.payload?.units));
  const seatList = seats();
  const result = new Map();
  unitList.forEach((unit) => {
    const position = unitPosition(unit);
    if (!Number.isFinite(position.x) || !Number.isFinite(position.y)) return;
    const seat = seatList.find((candidate, index) => {
      const unitId = firstDefined(candidate.unit_id, candidate.unitId);
      const seatId = firstDefined(candidate.seat, candidate.seat_id, candidate.id, index);
      return unitId === firstDefined(unit.id, unit.unit_id, unit.unitId)
        || Number(firstDefined(unit.seat, unit.seat_id, unit.seatId, -1)) === Number(seatId);
    });
    result.set(`${position.x},${position.y}`, { unit, seat });
  });
  arrayOf(state.minions).forEach((unit) => {
    const position = unitPosition(unit);
    if (!Number.isFinite(position.x) || !Number.isFinite(position.y)) return;
    result.set(`${position.x},${position.y}`, { unit, seat: null });
  });
  return result;
}

function renderBoard() {
  model.map = mapCells();
  $("cellCount").textContent = `${model.map.length || "—"} 格`;
  $("boardEmpty").hidden = model.map.length > 0;
  if (!model.map.length) {
    $("board").innerHTML = "";
    return;
  }

  const xValues = model.map.map((cell) => Number(firstDefined(cell.x, cell.q)));
  const rowValues = model.map.map((cell) => {
    const x = Number(firstDefined(cell.x, cell.q));
    const y = Number(firstDefined(cell.y, cell.r));
    return y + x / 2;
  });
  const minX = Math.min(...xValues);
  const maxX = Math.max(...xValues);
  const minRow = Math.min(...rowValues);
  const maxRow = Math.max(...rowValues);
  const xStep = 30;
  const yStep = 33;
  const occupants = unitsByPosition();
  const captainChoice = hasPrivateAction("resolve_captain_choice")
    ? stateRoot().pending_captain_choice
    : null;
  const captainCandidates = new Set(arrayOf(captainChoice?.candidate_ids));
  const reachable = new Map(
    arrayOf(privateView().reachable).map((cell) => [`${cell.x},${cell.y}`, cell.cost]),
  );
  const fastMoveTargets = new Set(
    arrayOf(privateView().fast_move_targets).map((cell) => `${cell.x},${cell.y}`),
  );
  const attackTargets = new Set(
    arrayOf(privateView().attack_targets).map((unit) => unit.unit_id),
  );
  const respawnTargets = new Set(
    arrayOf(privateView().respawn_targets).map((cell) => `${cell.x},${cell.y}`),
  );
  const teleportTargets = new Set(
    model.debugTeleport
      ? arrayOf(privateView().debug_teleport_targets).map((cell) => `${cell.x},${cell.y}`)
      : [],
  );

  $("board").style.width = `${(maxX - minX) * xStep + 48}px`;
  $("board").style.height = `${(maxRow - minRow) * yStep + 50}px`;
  $("board").innerHTML = model.map.map((cell) => {
    const x = Number(firstDefined(cell.x, cell.q));
    const y = Number(firstDefined(cell.y, cell.r));
    const row = y + x / 2;
    const region = firstDefined(cell.region, cell.zone, "mid");
    const state = String(firstDefined(cell.state, cell.kind, "empty"));
    const obstacle = Boolean(firstDefined(cell.obstacle, state === "terrain", false));
    const occupant = occupants.get(`${x},${y}`);
    const seatIndex = occupant?.seat
      ? Number(firstDefined(occupant.seat.seat, occupant.seat.seat_id, occupant.seat.id, 0))
      : null;
    const team = occupant?.seat
      ? seatTeam(occupant.seat, seatIndex)
      : seatTeam(occupant?.unit, 0);
    const pieceLabel = occupant?.seat
      ? `P${seatIndex + 1}`
      : ({ melee: "近", ranged: "远", heavy: "重" }[
        firstDefined(occupant?.unit?.kind, occupant?.unit?.type)
      ] || cleanText(firstDefined(occupant?.unit?.label), "兵").slice(0, 2));
    const occupantId = firstDefined(
      occupant?.unit?.unit_id,
      occupant?.unit?.unitId,
      occupant?.unit?.id,
    );
    const captainCandidateId = captainChoice?.kind === "remove_minions"
      && captainCandidates.has(occupantId)
      ? occupantId
      : captainChoice?.kind === "choose_spawn_cell"
        && captainCandidates.has(`${x},${y}`)
        ? `${x},${y}`
        : null;
    const debugMinionId = model.debugRemoveMinion && !occupant?.seat
      ? occupantId
      : null;
    const classes = [
      "hex",
      `region-${region}`,
      obstacle ? "obstacle" : "",
      cell.lane ? "lane" : "",
      state.endsWith("Spawn") || state.endsWith("_spawn") ? "spawn" : "",
      reachable.has(`${x},${y}`) ? "reachable" : "",
      fastMoveTargets.has(`${x},${y}`) ? "fast-move-target" : "",
      respawnTargets.has(`${x},${y}`) ? "respawn-target" : "",
      teleportTargets.has(`${x},${y}`) ? "debug-teleport-target" : "",
      captainCandidateId ? "captain-choice-target" : "",
      debugMinionId ? "debug-minion-target" : "",
      occupantId && attackTargets.has(occupantId) ? "attack-target" : "",
    ].filter(Boolean).join(" ");
    return `
      <div
        class="${escapeHtml(classes)}"
        style="left:${(x - minX) * xStep}px;top:${(row - minRow) * yStep}px"
        title="(${x}, ${y})"
        data-x="${x}" data-y="${y}"
        ${captainCandidateId ? `data-captain-candidate="${escapeHtml(captainCandidateId)}"` : ""}
        ${respawnTargets.has(`${x},${y}`) ? 'data-respawn="true"' : ""}
        ${debugMinionId ? `data-debug-minion="${escapeHtml(debugMinionId)}"` : ""}
        ${occupantId && attackTargets.has(occupantId) ? `data-attack-target="${escapeHtml(occupantId)}"` : ""}
      >
        ${reachable.has(`${x},${y}`) ? `<span class="move-cost">${reachable.get(`${x},${y}`)}</span>` : ""}
        ${occupant ? `<span class="piece team-${team} ${seatIndex === controlledSeatIndex() ? "controlled" : ""} ${occupant.seat ? "" : "minion"}">${escapeHtml(pieceLabel)}</span>` : ""}
      </div>
    `;
  }).join("");
  applyBoardScale();
}

function renderHand() {
  const hand = currentHand();
  $("hand").innerHTML = hand.length
    ? hand.map((card, index) => cardMarkup(card, index, true)).join("")
    : '<div class="empty-message">服务器尚未提供当前席位的初始手牌</div>';
  const confirmed = controlledSeatConfirmed();
  $("confirmButton").textContent = confirmed ? "已确认" : "确认选牌";
  $("confirmButton").disabled = !hasPrivateAction("confirm_selection") || model.pendingCommand;
}

function eventText(event) {
  if (typeof event === "string") return cleanText(event, "服务器事件");
  return cleanText(
    firstDefined(event.message, event.text, event.description),
    Object.entries(event)
      .filter(([key]) => !["type", "event_type", "eventType", "message"].includes(key))
      .slice(0, 4)
      .map(([key, value]) => `${key}: ${typeof value === "object" ? JSON.stringify(value) : value}`)
      .join(" · ") || "状态已更新",
  );
}

function renderLog() {
  const state = stateRoot();
  const events = arrayOf(firstDefined(
    model.payload?.events,
    model.payload?.log,
    state.events,
    state.log,
    state.history,
  ));
  $("eventCount").textContent = String(events.length);
  $("log").innerHTML = events.length ? [...events].reverse().map((event, index) => {
    const type = typeof event === "string" ? "Event" : firstDefined(event.type, event.event_type, event.eventType, "Event");
    const label = eventLabels[type] || cleanText(type, "服务器事件");
    return `
      <article class="log-entry">
        <span class="log-index">#${String(events.length - index).padStart(3, "0")}</span>
        <div>
          <strong>${escapeHtml(label)}</strong>
          <p>${escapeHtml(eventText(event))}</p>
        </div>
      </article>
    `;
  }).join("") : '<div class="empty-message">等待服务器事件</div>';
}

function renderCatalogFilters() {
  const filters = [
    { id: "all", name: "全部英雄" },
    ...model.heroes.map((hero, index) => ({
      id: String(firstDefined(hero.hero_id, hero.heroId, hero.id, `hero-${index}`)),
      name: heroName(hero, index),
    })),
  ];
  $("catalogFilters").innerHTML = filters.map((filter) => `
    <button
      class="catalog-filter ${model.catalogFilter === filter.id ? "active" : ""}"
      type="button"
      data-catalog-filter="${escapeHtml(filter.id)}"
    >${escapeHtml(filter.name)}</button>
  `).join("");
  document.querySelectorAll("[data-catalog-filter]").forEach((button) => {
    button.addEventListener("click", () => {
      model.catalogFilter = button.dataset.catalogFilter;
      renderCatalog();
    });
  });
}

function renderCatalog() {
  const filtered = model.catalogFilter === "all"
    ? model.cards
    : model.cards.filter((card) => String(firstDefined(card.hero_id, card.heroId)) === model.catalogFilter);
  $("catalogSummary").textContent = `${model.cards.length} 张正式卡牌 · 全部标记为“未实装未测试”`;
  $("catalogButton").textContent = `${model.cards.length || 108} 张卡目录`;
  renderCatalogFilters();
  $("catalog").innerHTML = filtered.length
    ? filtered.map((card, index) => cardMarkup(card, index, false)).join("")
    : '<div class="empty-message">当前筛选没有卡牌</div>';
}

function completeCardMarkup(card, index = 0, options = {}) {
  const action = primaryAction(card);
  const bonuses = options.bonuses || {};
  const color = firstDefined(card?.color_key, "silver");
  const level = card?.level == null ? "基础" : `Lv.${card.level}`;
  const subtype = firstDefined(action.subtype, {});
  const formatValue = (base, bonus = 0) => {
    if (base == null) return "—";
    const numericBonus = Number(bonus || 0);
    return numericBonus > 0 ? `${base}+${numericBonus}` : String(base);
  };
  const range = {
    label: cleanText(subtype?.type, "范围/远程"),
    value: formatValue(
      subtype?.value,
      bonuses[subtype.type === "远程" ? "ranged" : "range"],
    ),
  };
  const primaryBonusKey = {
    attack: "attack",
    defense: "defense",
    movement: "movement",
  }[action.family];
  const primaryValue = formatValue(action.value, bonuses[primaryBonusKey]);
  const movementValue = statValue(card, "movement");
  const defenseValue = statValue(card, "defense");
  const playedTurn = options.playedTurn;
  const discarded = Boolean(options.discarded);
  const selectable = Boolean(options.selectable);
  const defenseSelectable = Boolean(options.defenseSelectable);
  const showHero = options.showHero !== false;
  const selected = selectable && cardId(card) === selectedCardId();
  const unavailable = playedTurn || discarded;
  const disabled = !selectable
    || unavailable
    || (!hasPrivateAction("select_card") && !defenseSelectable)
    || model.pendingCommand;
  const tag = selectable ? "button" : "article";
  const attrs = selectable
    ? `type="button" data-card-id="${escapeHtml(cardId(card))}" ${defenseSelectable ? `data-defense-card="${escapeHtml(cardId(card))}"` : ""} aria-pressed="${selected}" ${disabled ? "disabled" : ""}`
    : "";
  const hero = cleanText(
    options.heroName || card?.hero,
    heroName(model.heroes.find((item) => item.hero_id === card?.hero_id) || {}, index),
  );
  return `
    <${tag} ${attrs} class="card color-${escapeHtml(color)} ${selectable ? "selectable" : ""} ${selected ? "selected" : ""} ${options.className || ""} ${playedTurn ? "card-played" : ""} ${discarded ? "card-discarded" : ""}">
      <div class="card-top">
        ${showHero ? `<span class="card-hero-name">${escapeHtml(hero)}</span>` : ""}
        <span>${escapeHtml(level)} · 先攻 ${escapeHtml(formatValue(card?.initiative, bonuses.initiative))}</span>
      </div>
      <span class="status-badge">${escapeHtml(options.statusLabel || "未实装未测试")}</span>
      <h3 title="${escapeHtml(apiCardName(card, index))}">${escapeHtml(apiCardName(card, index))}</h3>
      <p class="card-kind">${escapeHtml(cleanText(action.category, cleanText(action.family, "行动")))}</p>
      <div class="card-stats">
        <span>主要行动<strong>${escapeHtml(primaryValue)}</strong></span>
        <span>次要移动<strong>${escapeHtml(formatValue(movementValue, bonuses.movement))}</strong></span>
        <span>次要防御<strong>${escapeHtml(formatValue(defenseValue, bonuses.defense))}</strong></span>
        <span>${escapeHtml(range.label)}<strong>${escapeHtml(range.value)}</strong></span>
      </div>
      <p class="card-text">${escapeHtml(cleanText(action.text, "正式卡牌文本尚未提供。"))}</p>
      ${options.footerLabel ? `<p class="card-footer-note">${escapeHtml(options.footerLabel)}</p>` : ""}
      ${playedTurn ? `<span class="card-state-overlay">第 ${playedTurn} 回合打出</span>` : ""}
      ${discarded ? '<span class="card-state-overlay discarded">已弃置</span>' : ""}
    </${tag}>
  `;
}

function renderPlayersV2() {
  const current = controlledSeatIndex();
  const activeSeat = Number(firstDefined(stateRoot().active_seat, -1));
  const playerList = seats();
  $("players").innerHTML = [0, 1, 2, 3].map((slot) => {
    const seat = playerList.find((item, index) => Number(firstDefined(item.seat, index)) === slot);
    if (!seat) return `<article class="player-card"><div class="player-main"><h3>席位 ${slot + 1}</h3><p>等待服务器</p></div></article>`;
    const hero = seatHero(seat);
    const team = seatTeam(seat, slot);
    const selected = Boolean(seat.selected);
    const colors = arrayOf(seat.card_color_states);
    const passiveNames = {
      attack: "攻击",
      defense: "防御",
      movement: "移动",
      initiative: "先攻",
      range: "范围",
      ranged: "远程",
    };
    const passives = Object.entries(seat.passive_bonuses || {})
      .filter(([, value]) => Number(value) > 0)
      .map(([key, value]) => `${passiveNames[key] || key} +${value}`);
    const passiveSummary = passives.length ? passives.join(" · ") : "无总加成";
    return `
      <article class="player-card team-${team} ${slot === current ? "controlled" : ""} ${slot === activeSeat ? "active" : ""}">
        <div class="player-avatar">${escapeHtml(heroName(hero, slot).slice(0, 2))}</div>
        <div class="player-main">
          <h3>${escapeHtml(heroName(hero, slot))}</h3>
          <p>席位 ${slot + 1} · ${team === "blue" ? "蓝队" : "红队"}</p>
          <p>${escapeHtml(firstDefined(seat.coins, 0))} 金币 · ${selected ? "已选牌" : "待选牌"}</p>
        </div>
        <div class="player-card-colors">
          ${colors.map((item) => `<span class="card-color-dot color-${escapeHtml(item.color)} status-${escapeHtml(item.status)}" title="${escapeHtml(item.played_turn ? `第 ${item.played_turn} 回合打出` : item.status)}"></span>`).join("")}
        </div>
        <div class="player-passives passive-total">
          <span>总加成：${escapeHtml(passiveSummary)}</span>
        </div>
      </article>
    `;
  }).join("");
}

function renderPublicStatusV2(state) {
  const teams = arrayOf(state.teams);
  const blue = teams.find((team) => seatTeam(team) === "blue") || {};
  const red = teams.find((team) => seatTeam(team) === "red") || {};
  $("crystalStatus").innerHTML = `
    <span class="team-blue">蓝方水晶 ${escapeHtml(firstDefined(blue.crystal_life, 7))}</span>
    <span class="team-red">红方水晶 ${escapeHtml(firstDefined(red.crystal_life, 7))}</span>
  `;
  const blueMarks = Number(firstDefined(blue.frontline_marks, 0));
  const redMarks = Number(firstDefined(red.frontline_marks, 0));
  $("frontlineTrack").innerHTML = [0, 1, 2, 3, 4].map((index) => {
    const blueFilled = index < blueMarks;
    const redFilled = index >= 5 - redMarks;
    const status = blueFilled && redFilled ? "contested" : blueFilled ? "blue" : redFilled ? "red" : "";
    return `<span class="frontline-dot ${status}"></span>`;
  }).join("");
  const coin = firstDefined(state.decision_coin, state.decisionCoin);
  $("decisionCoin").className = `decision-coin ${coin === "blue" || coin === "red" ? coin : "neutral"}`;
}

function renderActionsV2() {
  const state = stateRoot();
  const currentPhase = phase();
  const revealed = initiativeEntries();
  const upgradeOptions = arrayOf(privateView().upgrade_options);
  const activeSeat = Number(firstDefined(state.active_seat, -1));
  const resolved = new Set(arrayOf(state.resolved_seats).map(Number));
  $("phaseTitle").textContent = phaseLabels[currentPhase] || currentPhase;
  $("roundLabel").textContent = `第 ${firstDefined(state.round, 1)} 轮`;
  const serverTurn = Number(firstDefined(state.turn, state.turn_number, state.turnNumber));
  $("turnLabel").textContent = Number.isFinite(serverTurn)
    ? `第 ${serverTurn} 回合`
    : "回合 --";
  $("statusLine").textContent = state.winner
    ? `${state.winner === "blue" ? "蓝队" : "红队"}获胜`
    : state.pending_initiative_choice
      ? `等待${state.pending_initiative_choice.team === "blue" ? "蓝队" : "红队"}队长决定同先攻行动顺序`
    : state.pending_captain_choice
      ? state.pending_captain_choice.kind === "remove_minions"
        ? `轮末差值移除：等待${state.pending_captain_choice.team === "blue" ? "蓝队" : "红队"}队长移除 ${state.pending_captain_choice.required_count} 个小兵`
        : `等待${state.pending_captain_choice.team === "blue" ? "蓝队" : "红队"}队长选择小兵出生位置`
    : state.pending_attack
      ? `你受到攻击：${state.pending_attack.base_attack} + ${state.pending_attack.support_bonus} - ${state.pending_attack.defense_reduction} = ${state.pending_attack.attack_value} 点`
    : currentPhase === "round_end" && state.round_end_step === "ready"
      ? "已进入轮末调试状态；可先调整小兵，再点击“开始轮末结算”。"
      : "合法操作和公开信息均由服务器提供。";
  renderPublicStatusV2(state);
  $("publicActions").innerHTML = upgradeOptions.length
    ? upgradeOptions.flatMap((option) =>
      arrayOf(option.candidates).map((candidate, index) => {
        const cardIdValue = firstDefined(candidate?.card_id, candidate);
        return completeCardMarkup(hydrateCard(cardIdValue, index), index, {
          className: "public-action-card upgrade-candidate",
          showHero: true,
          heroName: heroName(seatHero(controlledSeat()), controlledSeatIndex()),
          statusLabel: `${option.color.toUpperCase()} 升至 ${option.level} 级`,
          bonuses: controlledSeat()?.passive_bonuses,
          footerLabel: candidate?.gained_passive
            ? `选择此卡将获得：${candidate.gained_passive} +1`
            : "",
        });
      })).join("")
    : revealed.length
    ? revealed.map((card, index) => completeCardMarkup(card, index, {
      className: `public-action-card ${card.seat === activeSeat ? "active" : ""}`,
      heroName: heroName(seatHero(seats()[card.seat]), card.seat),
      showHero: true,
      bonuses: seats()[card.seat]?.passive_bonuses,
      statusLabel: `#${index + 1} · 席位 ${card.seat + 1}${resolved.has(card.seat) ? " · 已结算" : ""}`,
    })).join("")
    : '<div class="empty-message compact">等待四席确认选牌，完成后自动公开。</div>';

  const canSkip = hasPrivateAction("skip_action");
  const canChoose = hasPrivateAction("choose_action");
  const canComplete = hasPrivateAction("complete_action");
  const canCancel = hasPrivateAction("cancel_action");
  const canDefend = hasPrivateAction("resolve_defense");
  const canResolveInitiative = hasPrivateAction("resolve_initiative_choice");
  const canBeginRoundEnd = hasDebugAction("begin_round_end");
  const canSwitchToActive = currentPhase === "card_resolution"
    && activeSeat >= 0
    && controlledSeatIndex() !== activeSeat
    && !state.pending_attack
    && !state.pending_respawn
    && !state.pending_initiative_choice;
  const defenseCards = arrayOf(privateView().eligible_defense_cards);
  const initiativeChoice = privateView().pending_initiative_choice;
  const captainChoice = hasPrivateAction("resolve_captain_choice")
    ? state.pending_captain_choice
    : null;
  const options = arrayOf(privateView().action_options);
  const labels = { primary: "主要行动", secondary_movement: "次要移动" };
  $("serverActions").innerHTML = `
    <button id="switchActiveSeatButton" class="server-action ${canSwitchToActive ? "enabled" : ""}" type="button" ${canSwitchToActive && !model.pendingCommand ? "" : "disabled"}>切到当前行动者：席位 ${activeSeat + 1}</button>
    <button id="skipActionButton" class="server-action ${canSkip ? "enabled" : ""}" type="button" ${canSkip && !model.pendingCommand ? "" : "disabled"}>结束/跳过行动</button>
    <button id="cancelActionButton" class="server-action ${canCancel ? "enabled" : ""}" type="button" ${canCancel && !model.pendingCommand ? "" : "disabled"}>取消当前选择</button>
    <button id="completeActionButton" class="server-action ${canComplete ? "enabled" : ""}" type="button" ${canComplete && !model.pendingCommand ? "" : "disabled"}>完成未实装行动</button>
    <button id="declineDefenseButton" class="server-action ${canDefend ? "enabled danger" : ""}" type="button" ${canDefend && !model.pendingCommand ? "" : "disabled"}>不防御，英雄被击败</button>
    <button id="beginRoundEndButtonV2" class="server-action ${canBeginRoundEnd ? "enabled" : ""}" type="button" ${canBeginRoundEnd && !model.pendingCommand ? "" : "disabled"}>开始轮末结算</button>
    ${defenseCards.map((card) => `<button class="server-action ${canDefend ? "enabled" : ""}" type="button" data-defense-card-v2="${escapeHtml(card.card_id)}" ${canDefend && !model.pendingCommand ? "" : "disabled"}>弃置 ${escapeHtml(apiCardName(hydrateCard(card.card_id)))} 防御（${card.exclamation ? "任意" : card.defense_value}）</button>`).join("")}
    ${options.map((option, index) => `<button class="server-action ${canChoose ? "enabled" : ""}" type="button" data-action-option-v2="${index}" ${canChoose && !model.pendingCommand ? "" : "disabled"}>${option.kind === "fast_move" ? `快速移动（替代${option.source_slot === "primary" ? "主要移动" : "次要移动"}）` : labels[option.kind] || option.kind}</button>`).join("")}
    ${arrayOf(initiativeChoice?.candidate_seats).map((seat) => `<button class="server-action ${canResolveInitiative ? "enabled" : ""}" type="button" data-initiative-seat="${seat}" ${canResolveInitiative && !model.pendingCommand ? "" : "disabled"}>队长选择席位 ${Number(seat) + 1} 先行动</button>`).join("")}
    ${arrayOf(captainChoice?.candidate_ids).map((candidateId) => `<button class="server-action enabled" type="button" data-captain-candidate="${escapeHtml(candidateId)}" ${model.pendingCommand ? "disabled" : ""}>${captainChoice.kind === "remove_minions" ? "移除小兵" : "选择出生格"}：${escapeHtml(candidateId)}</button>`).join("")}
    ${upgradeOptions.flatMap((option) => arrayOf(option.candidates).map((candidate) => {
      const cardIdValue = firstDefined(candidate?.card_id, candidate);
      return `<button class="server-action enabled" type="button" data-upgrade-card="${escapeHtml(cardIdValue)}" data-upgrade-color="${escapeHtml(option.color)}" ${model.pendingCommand ? "disabled" : ""}>升级选择：${escapeHtml(apiCardName(hydrateCard(cardIdValue)))}</button>`;
    })).join("")}
  `;
  $("switchActiveSeatButton").addEventListener("click", () => {
    if (activeSeat >= 0) void joinSeat(activeSeat).catch(() => {});
  });
  $("skipActionButton").addEventListener("click", () => runCommand("/api/actions/skip", { seat: controlledSeatIndex() }));
  $("cancelActionButton").addEventListener("click", () => runCommand("/api/actions/cancel", { seat: controlledSeatIndex() }));
  $("completeActionButton").addEventListener("click", () => runCommand("/api/actions/complete", { seat: controlledSeatIndex() }));
  $("declineDefenseButton").addEventListener("click", () => runCommand("/api/actions/defend", {
    seat: controlledSeatIndex(),
    card_id: null,
  }));
  $("beginRoundEndButtonV2").addEventListener("click", () => runDebug("/api/debug/begin-round-end"));
  document.querySelectorAll("[data-defense-card-v2]").forEach((button) => {
    button.addEventListener("click", () => runCommand("/api/actions/defend", {
      seat: controlledSeatIndex(),
      card_id: button.dataset.defenseCardV2,
    }));
  });
  document.querySelectorAll("[data-action-option-v2]").forEach((button) => {
    button.addEventListener("click", () => {
      const option = options[Number(button.dataset.actionOptionV2)];
      runCommand("/api/actions/choose", { seat: controlledSeatIndex(), kind: option.kind, source_slot: option.source_slot });
    });
  });
  document.querySelectorAll("[data-initiative-seat]").forEach((button) => {
    button.addEventListener("click", () => runCommand("/api/actions/initiative-choice", {
      seat: controlledSeatIndex(),
      choice_id: initiativeChoice.choice_id,
      chosen_seat: Number(button.dataset.initiativeSeat),
    }));
  });
  document.querySelectorAll("[data-upgrade-card]").forEach((button) => {
    button.addEventListener("click", () => runCommand("/api/upgrade", {
      seat: controlledSeatIndex(),
      color: button.dataset.upgradeColor,
      card_id: button.dataset.upgradeCard,
    }));
  });
  document.querySelectorAll("[data-captain-candidate]").forEach((button) => {
    button.addEventListener("click", () => runCommand("/api/round-end/captain-choice", {
      seat: controlledSeatIndex(),
      choice_id: captainChoice.choice_id,
      candidate_id: button.dataset.captainCandidate,
    }));
  });
}

function renderHandV2() {
  const hand = currentHand();
  const used = arrayOf(privateView().used_card_ids);
  const discarded = new Set(arrayOf(privateView().discard));
  const eligibleDefense = new Set(
    arrayOf(privateView().eligible_defense_cards).map((item) => item.card_id),
  );
  $("hand").innerHTML = hand.length
    ? hand.map((card, index) => completeCardMarkup(card, index, {
      selectable: true,
      showHero: false,
      defenseSelectable: eligibleDefense.has(cardId(card)),
      playedTurn: used.indexOf(cardId(card)) >= 0 ? used.indexOf(cardId(card)) + 1 : null,
      discarded: discarded.has(cardId(card)),
      bonuses: controlledSeat()?.passive_bonuses,
    })).join("")
    : '<div class="empty-message">服务器尚未提供当前席位手牌。</div>';
  const confirmed = controlledSeatConfirmed();
  $("confirmButton").textContent = confirmed ? "已确认" : "确认选牌";
  $("confirmButton").disabled = !hasPrivateAction("confirm_selection") || model.pendingCommand;
}

function render() {
  renderRoom();
  renderPlayersV2();
  renderActionsV2();
  renderBoard();
  renderHandV2();
  renderLog();
  renderCatalog();
}

function showError(error) {
  $("toast").textContent = error.message || String(error);
  $("toast").hidden = false;
  window.clearTimeout(showError.timeout);
  showError.timeout = window.setTimeout(() => {
    $("toast").hidden = true;
  }, 5000);
}

async function refreshState() {
  model.payload = await api(stateUrl());
  render();
}

async function pollState() {
  if (model.pendingCommand || model.pollInFlight || !model.token) return;
  model.pollInFlight = true;
  try {
    const payload = await api(stateUrl());
    if (model.pendingCommand) return;
    model.payload = payload;
    renderConnection("online", "已连接 · 自动同步");
    render();
  } catch (error) {
    if (!model.pendingCommand) {
      renderConnection("error", "同步中断 · 正在重试");
    }
  } finally {
    model.pollInFlight = false;
  }
}

function startPolling() {
  window.clearInterval(model.pollTimer);
  model.pollTimer = window.setInterval(() => {
    void pollState();
  }, POLL_INTERVAL_MS);
}

async function joinSeat(seat, options = {}) {
  if (model.pendingCommand) return;
  const previousSeat = model.seat;
  const previousToken = model.token;
  const targetSeat = normalizeSeat(seat, previousSeat);
  const reconnectToken = targetSeat === previousSeat
    ? previousToken
    : cleanText(model.tokens[targetSeat], "");
  model.pendingCommand = true;
  model.seat = targetSeat;
  model.token = reconnectToken;
  renderConnection("connecting", `正在加入 ${model.roomId} · 席位 ${model.seat + 1}`);
  if (options.render !== false) render();
  try {
    let joined;
    try {
      joined = await post("/api/rooms/join", {
        room_id: model.roomId,
        seat: model.seat,
        token: reconnectToken || undefined,
      });
    } catch (error) {
      if (!reconnectToken || error.status !== 400) throw error;
      delete model.tokens[model.seat];
      model.token = "";
      persistIdentity();
      joined = await post("/api/rooms/join", {
        room_id: model.roomId,
        seat: model.seat,
      });
    }
    model.token = cleanText(
      firstDefined(joined?.token, joined?.access_token, joined?.session_token),
      "",
    );
    if (!model.token) throw new Error("加入房间成功，但服务器未返回 token");
    model.tokens[model.seat] = model.token;
    persistIdentity();
    if (options.updateUrl !== false) updateIdentityUrl();
    await refreshState();
    renderConnection("online", `已连接 · ${model.roomId} · 席位 ${model.seat + 1}`);
  } catch (error) {
    model.seat = previousSeat;
    model.token = previousToken;
    renderConnection("online", `席位切换失败，仍连接席位 ${previousSeat + 1}`);
    showError(error);
    await refreshState().catch(() => {});
    throw error;
  } finally {
    model.pendingCommand = false;
    render();
  }
}

async function runDebug(path, body = {}) {
  return runCommand(path, body);
}

function promptInteger(label, currentValue, minimum = 0) {
  const raw = window.prompt(label, String(currentValue));
  if (raw === null) return null;
  const value = Number(raw);
  if (!Number.isInteger(value) || value < minimum) {
    showError(new Error(`${label}必须是不小于 ${minimum} 的整数`));
    return null;
  }
  return value;
}

async function runCommand(path, body = {}) {
  if (model.pendingCommand) return;
  model.pendingCommand = true;
  render();
  try {
    const result = await post(path, commandBody(path, debugBody(body)));
    model.payload = result?.state || result?.seats || result?.events ? result : await api(stateUrl());
    renderConnection("online", "已连接 · 服务器权威状态");
  } catch (error) {
    if (error.status === 409) {
      showError(new Error("状态已更新，已刷新到最新版本"));
    } else {
      showError(error);
    }
    await refreshState().catch(() => {});
  } finally {
    model.pendingCommand = false;
    render();
  }
}

async function confirmSelection() {
  await runCommand("/api/cards/confirm", { seat: controlledSeatIndex() });
}

function selectCard(cardIdValue) {
  if (!hasPrivateAction("select_card")) return;
  runCommand("/api/cards/select", {
    seat: controlledSeatIndex(),
    card_id: cardIdValue,
  });
}

$("hand").addEventListener("click", (event) => {
  const card = event.target.closest("[data-card-id]");
  if (!card || card.disabled) return;
  if (card.dataset.defenseCard) {
    runCommand("/api/actions/defend", {
      seat: controlledSeatIndex(),
      card_id: card.dataset.defenseCard,
    });
    return;
  }
  selectCard(card.dataset.cardId);
});

$("board").addEventListener("click", (event) => {
  const respawnCell = event.target.closest(".hex.respawn-target");
  if (respawnCell) {
    runCommand("/api/actions/respawn", {
      seat: controlledSeatIndex(),
      x: Number(respawnCell.dataset.x),
      y: Number(respawnCell.dataset.y),
    });
    return;
  }
  const attackTarget = event.target.closest(".hex.attack-target");
  if (attackTarget) {
    runCommand("/api/actions/attack", {
      seat: controlledSeatIndex(),
      target_id: attackTarget.dataset.attackTarget,
    });
    return;
  }
  const debugMinion = event.target.closest(".hex.debug-minion-target");
  if (debugMinion) {
    model.debugRemoveMinion = false;
    runDebug("/api/debug/remove-minion", {
      seat: controlledSeatIndex(),
      minion_id: debugMinion.dataset.debugMinion,
    });
    return;
  }
  const captainCell = event.target.closest(".hex.captain-choice-target");
  if (captainCell) {
    const choice = stateRoot().pending_captain_choice;
    runCommand("/api/round-end/captain-choice", {
      seat: controlledSeatIndex(),
      choice_id: choice.choice_id,
      candidate_id: captainCell.dataset.captainCandidate,
    });
    return;
  }
  const teleportCell = event.target.closest(".hex.debug-teleport-target");
  if (teleportCell) {
    model.debugTeleport = false;
    runDebug("/api/debug/teleport", {
      seat: controlledSeatIndex(),
      x: Number(teleportCell.dataset.x),
      y: Number(teleportCell.dataset.y),
    });
    return;
  }
  const fastMoveCell = event.target.closest(".hex.fast-move-target");
  if (fastMoveCell) {
    runCommand("/api/actions/move", {
      seat: controlledSeatIndex(),
      x: Number(fastMoveCell.dataset.x),
      y: Number(fastMoveCell.dataset.y),
    });
    return;
  }
  const cell = event.target.closest(".hex.reachable");
  if (!cell) return;
  runCommand("/api/actions/move", {
    seat: controlledSeatIndex(),
    x: Number(cell.dataset.x),
    y: Number(cell.dataset.y),
  });
});

function applyBoardScale() {
  $("board").style.transformOrigin = "0 0";
  $("board").style.transform = `scale(${model.boardScale})`;
  $("boardZoom").textContent = `滚轮缩放 · ${Math.round(model.boardScale * 100)}%`;
}

$("boardViewport").addEventListener("wheel", (event) => {
  if (!model.map.length) return;
  event.preventDefault();
  const viewport = $("boardViewport");
  const oldScale = model.boardScale;
  const step = event.deltaY < 0 ? 0.1 : -0.1;
  const nextScale = Math.min(1.8, Math.max(0.6, oldScale + step));
  if (nextScale === oldScale) return;
  const bounds = viewport.getBoundingClientRect();
  const cursorX = event.clientX - bounds.left;
  const cursorY = event.clientY - bounds.top;
  model.boardScale = Number(nextScale.toFixed(1));
  applyBoardScale();
  const ratio = model.boardScale / oldScale;
  viewport.scrollLeft = (viewport.scrollLeft + cursorX) * ratio - cursorX;
  viewport.scrollTop = (viewport.scrollTop + cursorY) * ratio - cursorY;
}, { passive: false });

$("confirmButton").addEventListener("click", () => {
  confirmSelection();
});

$("seatSelect").addEventListener("change", (event) => {
  void joinSeat(Number(event.target.value)).catch(() => {});
});

[
  "setupCrystalLife",
  "setupFrontlineMarks",
  "setupHero0",
  "setupHero1",
  "setupHero2",
  "setupHero3",
].forEach((id) => {
  $(id).addEventListener("focus", () => {
    model.setupDirty = true;
  });
  $(id).addEventListener("input", () => {
    model.setupDirty = true;
  });
  $(id).addEventListener("change", () => {
    model.setupDirty = true;
  });
});

$("resetButton").addEventListener("click", () => {
  runDebug("/api/debug/reset");
});

$("configureRoomButton").addEventListener("click", async () => {
  await runCommand("/api/rooms/configure", {
    starting_crystal_life: Number($("setupCrystalLife").value),
    frontline_victory_marks: Number($("setupFrontlineMarks").value),
    hero_ids: [0, 1, 2, 3].map((seat) => $(`setupHero${seat}`).value),
  });
  model.setupDirty = false;
  render();
});

$("forceConfirmButton").addEventListener("click", () => {
  runDebug("/api/debug/force-confirm");
});

$("teleportButton").addEventListener("click", () => {
  model.debugTeleport = !model.debugTeleport;
  render();
});

$("debugSkipCurrentButton").addEventListener("click", () => {
  runDebug("/api/debug/skip-current");
});

$("debugSkipAllButton").addEventListener("click", () => {
  runDebug("/api/debug/skip-all");
});

$("debugCoinsButton").addEventListener("click", () => {
  const value = promptInteger("设置当前席位金币", firstDefined(controlledSeat()?.coins, 0));
  if (value !== null) runDebug("/api/debug/set-coins", { seat: controlledSeatIndex(), coins: value });
});

$("debugCrystalButton").addEventListener("click", () => {
  const team = controlledSeatIndex() % 2 === 0 ? "blue" : "red";
  const teamState = arrayOf(stateRoot().teams).find((item) => item.team === team) || {};
  const value = promptInteger(`设置${team === "blue" ? "蓝" : "红"}方水晶生命`, firstDefined(teamState.crystal_life, 7));
  if (value !== null) runDebug("/api/debug/set-crystal", { team, crystal_life: value });
});

$("debugFrontlineButton").addEventListener("click", () => {
  const team = controlledSeatIndex() % 2 === 0 ? "blue" : "red";
  const teamState = arrayOf(stateRoot().teams).find((item) => item.team === team) || {};
  const value = promptInteger(`设置${team === "blue" ? "蓝" : "红"}方战线标记`, firstDefined(teamState.frontline_marks, 0));
  if (value !== null) runDebug("/api/debug/set-frontline", { team, frontline_marks: value });
});

$("debugDefeatButton").addEventListener("click", () => {
  runDebug("/api/debug/defeat-hero", { seat: controlledSeatIndex() });
});

$("debugResetMinionsButton").addEventListener("click", () => {
  runDebug("/api/debug/reset-minions");
});

$("debugAdvanceBlueButton").addEventListener("click", () => {
  runDebug("/api/debug/advance-frontline", { team: "blue" });
});

$("debugAdvanceRedButton").addEventListener("click", () => {
  runDebug("/api/debug/advance-frontline", { team: "red" });
});

$("enterRoundEndButton").addEventListener("click", () => {
  model.debugTeleport = false;
  model.debugRemoveMinion = false;
  runDebug("/api/debug/enter-round-end");
});

$("removeMinionButton").addEventListener("click", () => {
  model.debugRemoveMinion = !model.debugRemoveMinion;
  model.debugTeleport = false;
  render();
});

$("catalogButton").addEventListener("click", () => {
  $("catalogOverlay").hidden = false;
  document.body.style.overflow = "hidden";
});

$("catalogClose").addEventListener("click", () => {
  $("catalogOverlay").hidden = true;
  document.body.style.overflow = "";
});

$("catalogOverlay").addEventListener("click", (event) => {
  if (event.target === $("catalogOverlay")) $("catalogClose").click();
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && !$("catalogOverlay").hidden) $("catalogClose").click();
});

async function boot() {
  try {
    const catalog = await api("/api/catalog");
    model.catalog = catalog;
    model.heroes = catalogHeroes(catalog);
    model.cards = catalogCards(catalog, model.heroes);
    await joinSeat(model.seat, { updateUrl: true, render: false });
    startPolling();
  } catch (error) {
    renderConnection("error", "连接失败");
    if (!$("toast").hidden) return;
    showError(error);
  }
}

boot();
