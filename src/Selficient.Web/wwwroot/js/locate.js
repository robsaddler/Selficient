// @ts-check
/**
 * "Use my location" beside a postcode box. The browser asks the person for their position once,
 * the server asks postcodes.io for the nearest postcode and sends it back, and the box is filled.
 * Nothing is kept: the coordinates go to the server rounded and leave it the same second. Without
 * script the button is hidden and the box is typed into as before.
 */

/**
 * @param {Document} doc
 */
export function initLocate(doc) {
  for (const button of doc.querySelectorAll("[data-locate]")) {
    if (!(button instanceof HTMLButtonElement)) {
      continue;
    }
    button.addEventListener("click", () => {
      if ("geolocation" in navigator) {
        void locate(doc, button);
      } else {
        const status = doc.querySelector(button.dataset.locateStatus ?? "");
        if (status instanceof HTMLElement) {
          status.textContent = "This browser cannot share its location. Type the postcode instead.";
          status.hidden = false;
        }
      }
    });
  }
}

/**
 * @param {Document} doc
 * @param {HTMLButtonElement} button
 */
async function locate(doc, button) {
  // The box named by the button, or failing that the postcode box nearest it in the same form.
  const named = doc.querySelector(button.dataset.locateFor ?? "");
  const input = named instanceof HTMLInputElement ? named : button.closest("form")?.querySelector("input[autocomplete='postal-code']");
  const status = doc.querySelector(button.dataset.locateStatus ?? "");
  if (!(input instanceof HTMLInputElement)) {
    return;
  }
  const submits = button.hasAttribute("data-locate-submit");
  const say = (/** @type {string} */ sentence) => {
    if (status instanceof HTMLElement) {
      status.textContent = sentence;
      status.hidden = sentence === "";
    }
  };

  button.disabled = true;
  say("Finding the postcode where you are.");
  try {
    const position = await new Promise((resolve, reject) => {
      navigator.geolocation.getCurrentPosition(resolve, reject, { enableHighAccuracy: false, timeout: 10_000, maximumAge: 300_000 });
    });
    const { latitude, longitude } = /** @type {GeolocationPosition} */ (position).coords;
    const response = await fetch("/api/geo/nearest", {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ lat: Number(latitude.toFixed(4)), lng: Number(longitude.toFixed(4)) }),
    });
    const answer = await response.json();
    if (response.ok && typeof answer?.postcode === "string") {
      input.value = answer.postcode;
      input.dispatchEvent(new Event("input", { bubbles: true }));
      if (submits && input.form) {
        // On the landing page the postcode is the whole form, so finding it is starting the plan.
        say(`Your postcode is ${answer.postcode}. Opening the questions.`);
        input.form.requestSubmit();
      } else {
        say(`Your postcode is ${answer.postcode}. Check it and carry on.`);
      }
    } else {
      say(typeof answer?.message === "string" ? answer.message : "We couldn't find a postcode where you are. Type it in instead.");
    }
  } catch (error) {
    say(error instanceof GeolocationPositionError && error.code === error.PERMISSION_DENIED
      ? "Your browser did not share your location. Type the postcode instead."
      : "We couldn't work out where you are. Type the postcode instead.");
  } finally {
    button.disabled = false;
  }
}
