// @ts-check
/**
 * The figures a household may give its own value for, as the product ships them: the answer
 * /api/figures gives when nothing has been published. They are here, in the script, so a browser
 * that has never reached the server still has them. A test compares this object with the server's
 * own list, so it cannot drift: change a shipped figure in the code and this file must follow.
 */

/** @typedef {{ id: string, question: string, unit: string, min: number, max: number, whole: boolean, value: number }} Figure */
/** @typedef {{ version: number, publishedAt: string | null, figures: Figure[] }} Figures */

/** @type {Figures} */
export const SHIPPED = {
  "version": 0,
  "publishedAt": null,
  "figures": [
    { "id": "assumed.cash-pounds-a-person-a-day", "question": "What does a person need in cash for a day, when only cash will do?", "unit": "whole pounds", "min": 1, "max": 100, "whole": true, "value": 10 },
    { "id": "water-butt.stock", "question": "How many litres does your water butt hold?", "unit": "litres", "min": 0, "max": 4200, "whole": false, "value": 210 },
    { "id": "closed-freezer.stock", "question": "How many days does your freezer keep its cold with the door shut?", "unit": "days", "min": 0, "max": 100, "whole": false, "value": 2 },
    { "id": "wood-stove.stock", "question": "How many days of fires does a full log store give you?", "unit": "days", "min": 0, "max": 420, "whole": false, "value": 21 },
    { "id": "stairlift-batteries.stock", "question": "How many days of trips to bed and the bathroom does your stairlift make in a power cut?", "unit": "days", "min": 0, "max": 100, "whole": false, "value": 2 },
    { "id": "petrol-car.stock", "question": "How many days of your usual driving is half a tank?", "unit": "days", "min": 0, "max": 140, "whole": false, "value": 7 },
    { "id": "electric-car.stock", "question": "How many days of your usual driving is an ordinary charge?", "unit": "days", "min": 0, "max": 100, "whole": false, "value": 5 },
    { "id": "portable-power-station.store-wh", "question": "How many watt-hours does your power station hold?", "unit": "watt-hours", "min": 0, "max": 200000, "whole": false, "value": 1024 }
  ]
};
