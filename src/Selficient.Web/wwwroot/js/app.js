// @ts-check
/**
 * Selficient entry point. One module per page concern, each registered here. Behaviour attaches by
 * element or data attribute, never by route, and everything degrades: the server renders a complete
 * page before this file runs.
 */
import { DaysReady } from "selficient/elements/days-ready.js";
import { initTheme } from "selficient/theme.js";
import { initDialogs } from "selficient/dialogs.js";
import { initPlanPage } from "selficient/plan-page.js";

if (!customElements.get("days-ready")) {
  customElements.define("days-ready", DaysReady);
}

initTheme(document);
initDialogs(document);
void initPlanPage(document);
