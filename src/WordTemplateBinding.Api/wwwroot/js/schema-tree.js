const fieldMimeType = "application/x-word-template-field";

export function renderSchemaTree(container, nodes) {
  container.replaceChildren();
  for (const node of nodes) {
    container.append(createNode(node));
  }
}

function createNode(node) {
  const wrapper = document.createElement("div");
  wrapper.className = "schema-node";

  const row = document.createElement("div");
  row.className = "schema-row";
  const toggle = document.createElement("button");
  toggle.type = "button";
  toggle.className = "schema-toggle";
  toggle.setAttribute("aria-label", `展开 ${node.name}`);
  toggle.textContent = node.children.length ? "▸" : "·";

  const label = document.createElement("button");
  label.type = "button";
  label.className = "schema-label";
  label.draggable = Boolean(node.isLeaf && node.isBindable);
  const name = document.createElement("strong");
  name.textContent = node.name;
  const path = document.createElement("small");
  path.textContent = node.path;
  label.append(name, path);

  const type = document.createElement("span");
  type.className = "type-badge";
  type.textContent = node.type;
  row.append(toggle, label, type);
  wrapper.append(row);

  let childrenContainer = null;
  let expanded = false;
  if (node.children.length) {
    toggle.addEventListener("click", () => {
      expanded = !expanded;
      toggle.textContent = expanded ? "▾" : "▸";
      toggle.setAttribute("aria-expanded", String(expanded));
      if (!childrenContainer) {
        childrenContainer = document.createElement("div");
        childrenContainer.className = "schema-children";
        for (const child of node.children) {
          childrenContainer.append(createNode(child));
        }
        wrapper.append(childrenContainer);
      }
      childrenContainer.hidden = !expanded;
    });
  } else {
    toggle.disabled = true;
  }

  if (label.draggable) {
    label.title = "拖拽到中间高亮的模拟数据上";
    label.addEventListener("dragstart", (event) => {
      event.dataTransfer.effectAllowed = "copy";
      event.dataTransfer.setData(
        fieldMimeType,
        JSON.stringify({
          name: node.name,
          path: node.path,
          type: node.type,
        }),
      );
    });
  } else if (node.isLeaf) {
    label.title = "该字段属于数组或当前阶段不支持绑定";
  }

  return wrapper;
}
