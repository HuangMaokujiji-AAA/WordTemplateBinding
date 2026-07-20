export function renderBindingList(container, mockItems, handlers) {
  container.replaceChildren();
  const boundItems = mockItems.filter((item) => item.isBound);
  if (!boundItems.length) {
    const empty = document.createElement("p");
    empty.className = "empty-state";
    empty.textContent = "尚无绑定关系";
    container.append(empty);
    return;
  }

  for (const item of boundItems) {
    const card = document.createElement("article");
    card.className = "binding-card";
    const main = document.createElement("button");
    main.type = "button";
    main.className = "binding-main";
    const value = document.createElement("strong");
    value.textContent = `模拟值：${item.mockValue}`;
    const path = document.createElement("span");
    path.textContent = item.boundDataPath;
    main.append(value, path);
    main.addEventListener("click", () => handlers.onFocus(item));

    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "binding-remove";
    remove.textContent = "取消绑定";
    remove.addEventListener("click", () => handlers.onDelete(item.locatorId));
    card.append(main, remove);
    container.append(card);
  }
}

export function renderProperties(container, item) {
  container.replaceChildren();
  if (!item) {
    const empty = document.createElement("p");
    empty.className = "empty-state";
    empty.textContent = "点击中间的高亮模拟数据查看属性";
    container.append(empty);
    return;
  }

  const list = document.createElement("dl");
  appendProperty(list, "LocatorId", item.locatorId);
  appendProperty(list, "原始值", item.mockValue);
  appendProperty(list, "模拟数据类型", item.dataType);
  appendProperty(list, "段落索引", String(item.locator.paragraphIndex));
  appendProperty(list, "起始偏移", String(item.locator.startOffset));
  appendProperty(list, "已绑定字段", item.boundDataPath || "未绑定");
  appendProperty(list, "字段类型", item.boundDataType || "—");
  container.append(list);
}

function appendProperty(list, label, value) {
  const wrapper = document.createElement("div");
  const term = document.createElement("dt");
  term.textContent = label;
  const description = document.createElement("dd");
  description.textContent = value;
  wrapper.append(term, description);
  list.append(wrapper);
}
