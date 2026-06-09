const state = {
  draft: null,
  heroIndex: 0,
  cardIndex: 0,
  dirty: false,
};

const colorOptions = ["金", "银", "红", "绿", "蓝", "紫"];
const categoryOptions = ["基础攻击", "攻击", "基础技能", "技能", "终极技能", "防御", "移动", "防御/技能"];
const subtypeOptions = ["", "范围", "远程"];
const passiveOptions = ["", "攻击", "防御", "先攻", "移动", "范围", "远程"];

const els = {
  fileStatus: document.querySelector("#fileStatus"),
  heroList: document.querySelector("#heroList"),
  reloadBtn: document.querySelector("#reloadBtn"),
  saveBtn: document.querySelector("#saveBtn"),
  currentPath: document.querySelector("#currentPath"),
  dirtyState: document.querySelector("#dirtyState"),
  cardForm: document.querySelector("#cardForm"),
  initiative: document.querySelector("#initiativeInput"),
  cardName: document.querySelector("#cardNameInput"),
  level: document.querySelector("#levelInput"),
  color: document.querySelector("#colorInput"),
  secondaryMove: document.querySelector("#secondaryMoveInput"),
  secondaryDefense: document.querySelector("#secondaryDefenseInput"),
  primaryCategory: document.querySelector("#primaryCategoryInput"),
  primaryValue: document.querySelector("#primaryValueInput"),
  exclamation: document.querySelector("#exclamationInput"),
  subtypeType: document.querySelector("#subtypeTypeInput"),
  subtypeValue: document.querySelector("#subtypeValueInput"),
  primaryText: document.querySelector("#primaryTextInput"),
  passive: document.querySelector("#passiveInput"),
};

function fillNumberSelect(select, min, max) {
  select.replaceChildren();
  for (let value = min; value <= max; value += 1) {
    const option = document.createElement("option");
    option.value = String(value);
    option.textContent = String(value);
    select.append(option);
  }
}

function fillTextSelect(select, values, current = null) {
  const set = new Set(values);
  if (current !== null && current !== undefined && !set.has(current)) {
    set.add(current);
  }
  select.replaceChildren();
  for (const value of set) {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = value || "空";
    select.append(option);
  }
}

function setupInputs() {
  fillNumberSelect(els.initiative, 0, 15);
  fillNumberSelect(els.secondaryMove, 0, 10);
  fillNumberSelect(els.secondaryDefense, 0, 10);
  fillNumberSelect(els.primaryValue, 0, 13);
  fillNumberSelect(els.subtypeValue, 0, 8);
  fillTextSelect(els.color, colorOptions);
  fillTextSelect(els.primaryCategory, categoryOptions);
  fillTextSelect(els.subtypeType, subtypeOptions);
  fillTextSelect(els.passive, passiveOptions);

  els.level.replaceChildren();
  for (const [value, label] of [["", "无"], ["1", "1"], ["2", "2"], ["3", "3"], ["4", "4"]]) {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = label;
    els.level.append(option);
  }
}

async function api(path, body = {}) {
  const response = await fetch(path, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  });
  const data = await response.json();
  if (!response.ok) {
    throw new Error(data.error || "请求失败");
  }
  return data;
}

async function loadDraft() {
  els.fileStatus.textContent = "正在载入";
  state.draft = await api("/api/card-draft/load");
  state.heroIndex = 0;
  state.cardIndex = 0;
  state.dirty = false;
  renderLibrary();
  renderCurrentCard();
  updateStatus();
}

function currentHero() {
  return state.draft?.heroes?.[state.heroIndex] ?? null;
}

function currentCard() {
  return currentHero()?.cards?.[state.cardIndex] ?? null;
}

function levelLabel(card) {
  if (card.level === null || card.level === undefined) return "无等级";
  return `${card.level}级`;
}

function colorClass(color) {
  return colorOptions.includes(color) ? color : "";
}

function renderLibrary() {
  els.heroList.replaceChildren();
  if (!state.draft) return;
  state.draft.heroes.forEach((hero, heroIndex) => {
    const group = document.createElement("article");
    group.className = "hero-group";

    const title = document.createElement("div");
    title.className = "hero-title";
    title.innerHTML = `<span>${hero.name || hero.hero_id || "未命名英雄"}</span><span>${hero.cards?.length || 0} 张</span>`;
    group.append(title);

    const list = document.createElement("div");
    list.className = "card-list";
    (hero.cards || []).forEach((card, cardIndex) => {
      const button = document.createElement("button");
      button.type = "button";
      button.className = `card-nav ${heroIndex === state.heroIndex && cardIndex === state.cardIndex ? "active" : ""}`;
      const chip = document.createElement("span");
      chip.className = `color-chip ${colorClass(card.color)}`;
      chip.textContent = card.color || "?";
      const name = document.createElement("span");
      name.className = "card-nav-name";
      name.textContent = card.name || card.card_name || "未命名卡牌";
      const level = document.createElement("span");
      level.className = "card-nav-level";
      level.textContent = levelLabel(card);
      button.append(chip, name, level);
      button.addEventListener("click", () => {
        updateCurrentFromForm();
        state.heroIndex = heroIndex;
        state.cardIndex = cardIndex;
        renderLibrary();
        renderCurrentCard();
        updateStatus();
      });
      list.append(button);
    });
    group.append(list);
    els.heroList.append(group);
  });
}

function clampNumber(value, min, max) {
  const num = Number(value);
  if (!Number.isFinite(num)) return min;
  return Math.max(min, Math.min(max, num));
}

function setSelect(select, value) {
  const stringValue = value === null || value === undefined ? "" : String(value);
  if (![...select.options].some((option) => option.value === stringValue)) {
    const option = document.createElement("option");
    option.value = stringValue;
    option.textContent = stringValue || "空";
    select.append(option);
  }
  select.value = stringValue;
}

function secondaryValue(slot) {
  if (!slot || !slot.has_action) return 0;
  return clampNumber(slot.value ?? 0, 0, 10);
}

function renderCurrentCard() {
  const hero = currentHero();
  const card = currentCard();
  const disabled = !card;
  [...els.cardForm.elements].forEach((input) => {
    input.disabled = disabled;
  });
  if (!card) {
    els.currentPath.textContent = "未选择卡牌";
    return;
  }

  fillTextSelect(els.color, colorOptions, card.color);
  fillTextSelect(els.primaryCategory, categoryOptions, card.primary_action?.category);
  fillTextSelect(els.subtypeType, subtypeOptions, card.primary_action?.subtype?.type || "");
  fillTextSelect(els.passive, passiveOptions, card.passive_bonus?.type || "");

  els.currentPath.textContent = `${hero.name || hero.hero_id} / ${card.name || card.card_name || "未命名卡牌"}`;
  setSelect(els.initiative, clampNumber(initiativeValue(card), 0, 15));
  els.cardName.value = card.name || card.card_name || "";
  setSelect(els.level, card.level === null || card.level === undefined ? "" : card.level);
  setSelect(els.color, card.color || "");
  setSelect(els.secondaryMove, secondaryValue(card.secondary_actions?.movement));
  setSelect(els.secondaryDefense, secondaryValue(card.secondary_actions?.defense));
  setSelect(els.primaryCategory, card.primary_action?.category || "");
  setSelect(els.primaryValue, clampNumber(card.primary_action?.value ?? 0, 0, 13));
  setSelect(els.exclamation, card.primary_action?.exclamation ? "true" : "false");
  setSelect(els.subtypeType, card.primary_action?.subtype?.type || "");
  setSelect(els.subtypeValue, clampNumber(card.primary_action?.subtype?.value ?? 0, 0, 8));
  els.primaryText.value = card.primary_action?.text || "";
  setSelect(els.passive, card.passive_bonus?.type || "");
}

function initiativeValue(card) {
  if (typeof card.initiative === "number") return card.initiative;
  return card.initiative?.value ?? 0;
}

function writeSecondary(slot, value, type) {
  const num = clampNumber(value, 0, 10);
  slot.type = type;
  slot.has_action = num > 0;
  slot.value = num > 0 ? num : null;
}

function updateCurrentFromForm() {
  const card = currentCard();
  if (!card) return;
  card.name = els.cardName.value.trim();
  delete card.card_name;
  card.hero = currentHero()?.name || card.hero;
  card.color = els.color.value || null;
  card.level = els.level.value === "" ? null : Number(els.level.value);
  card.initiative = Number(els.initiative.value);

  card.secondary_actions ||= {};
  card.secondary_actions.movement ||= { type: "移动", has_action: false, value: null };
  card.secondary_actions.defense ||= { type: "防御", has_action: false, value: null };
  writeSecondary(card.secondary_actions.movement, els.secondaryMove.value, "移动");
  writeSecondary(card.secondary_actions.defense, els.secondaryDefense.value, "防御");

  const subtypeType = els.subtypeType.value;
  card.primary_action ||= {};
  card.primary_action.category = els.primaryCategory.value || null;
  card.primary_action.family = actionFamilyForCategory(card.primary_action.category);
  card.primary_action.text = els.primaryText.value;
  card.primary_action.value = Number(els.primaryValue.value);
  card.primary_action.exclamation = els.exclamation.value === "true";
  card.primary_action.subtype = subtypeType
    ? { type: subtypeType, value: Number(els.subtypeValue.value) }
    : null;

  card.passive_bonus = { type: els.passive.value || null };
}

function actionFamilyForCategory(category) {
  if (category === "基础攻击" || category === "攻击") return "attack";
  if (category === "基础技能" || category === "技能") return "skill";
  if (category === "终极技能") return "ultimate";
  if (category === "防御/技能") return "defense_skill";
  if (category === "防御") return "defense";
  if (category === "移动") return "movement";
  return null;
}

function markDirty() {
  updateCurrentFromForm();
  state.dirty = true;
  renderLibrary();
  updateStatus();
}

function updateStatus() {
  if (!state.draft) {
    els.fileStatus.textContent = "等待载入";
    els.dirtyState.textContent = "";
    return;
  }
  const heroCount = state.draft.heroes?.length || 0;
  const cardCount = state.draft.heroes?.reduce((sum, hero) => sum + (hero.cards?.length || 0), 0) || 0;
  els.fileStatus.textContent = `${heroCount} 个英雄，${cardCount} 张卡`;
  els.dirtyState.textContent = state.dirty ? "有未保存修改" : "没有未保存修改";
  els.saveBtn.disabled = !state.dirty;
}

async function saveDraft() {
  updateCurrentFromForm();
  els.saveBtn.disabled = true;
  els.fileStatus.textContent = "正在保存";
  await api("/api/card-draft/save", { draft: state.draft });
  state.dirty = false;
  updateStatus();
}

function bindEvents() {
  els.reloadBtn.addEventListener("click", async () => {
    if (state.dirty && !confirm("当前有未保存修改，确定重新载入并放弃这些修改吗？")) return;
    await loadDraft();
  });
  els.saveBtn.addEventListener("click", saveDraft);
  els.cardForm.addEventListener("input", markDirty);
  els.cardForm.addEventListener("change", markDirty);
  window.addEventListener("beforeunload", (event) => {
    if (!state.dirty) return;
    event.preventDefault();
    event.returnValue = "";
  });
}

setupInputs();
bindEvents();
loadDraft().catch((error) => {
  els.fileStatus.textContent = `载入失败：${error.message}`;
});
