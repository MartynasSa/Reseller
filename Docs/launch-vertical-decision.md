# Launch Vertical Decision

**Date:** 2026-08-21
**Status:** Decided

## Decision

Launch vertical: **Phone / consumer electronics resellers** (arbitrage buyers who source
used phones, tablets, laptops, and small consumer electronics from classified marketplaces
to refurbish/flip or resell on eBay/Amazon/Swappa).

## Options considered

| Vertical | eBay coverage | Craigslist coverage | Keyword/category precision | Ease of finding concierge client #1 |
|---|---|---|---|---|
| **Phone/electronics resellers** | Strong — dedicated categories, rich item specifics (brand/model/storage/condition), real Browse API | Strong — "electronics" category in every metro | High — exact model numbers ("iPhone 13 Pro 256GB", "Galaxy S23 Ultra") | Easy — active, tool-buying communities (r/flipping, reselling Discords/FB groups) |
| Used car dealers | Strong (eBay Motors) but dealers already use vAuto/Homenet/Marketplace | Strong | Medium — trim/mileage/VIN nuance | Medium — dealers harder to cold-reach, high trust bar |
| Furniture flippers | Weak (bulky, shipping-unfriendly, thin category) | Strong | Low — style/condition is subjective | Medium |
| Sneaker resellers | Weak fit — resale liquidity is on StockX/GOAT, not eBay/CL | Weak | High (exact SKUs) but wrong marketplaces | Hard — market already saturated with bots for the platforms that matter |
| Industrial equipment dealers | Strong (dedicated eBay categories) | Inconsistent/regional | Medium | Hard — no single online community hangout, longer sales cycle |

## Why phone/electronics resellers

1. **Marketplace fit**: Both launch sources (eBay, Craigslist) genuinely cover this niche's
   sourcing behavior — eBay has structured categories and rich item-specifics data via the
   Browse API (brand, model, storage, carrier, condition), and Craigslist's electronics
   category is active in every metro. Furniture and industrial equipment only really work on
   one of the two sources; cars need title/VIN verification that adds fraud-liability risk
   for v0.
2. **Keyword/category precision**: Phones and electronics have exact, well-known model
   identifiers (e.g. "iPhone 13 Pro Max 256GB unlocked", "Samsung Galaxy S23 Ultra",
   "MacBook Air M2 13-inch"). This makes a rule-based v0 filter (no AI/ML per the MVP scope)
   tractable — match on model string + storage/spec token + condition keyword, rather than
   subjective attributes like furniture "style" or car "trim level."
3. **Concierge outreach**: This buyer segment congregates in known, reachable online
   communities (r/flipping, r/Flipping-adjacent Discords, phone-reselling Facebook groups,
   local phone-repair/refurb shop networks) and already pays for sourcing/arbitrage tooling
   (SellerAmp, Keepa, Tactical Arbitrage for Amazon-side scouting), so willingness to pay for
   a marketplace-alert tool is a proven behavior, not something to be convinced of from
   scratch.

## How this shapes the MVP

- **eBay Browse API integration**: prioritize category filters for Cell Phones & Smartphones,
  Tablets & eReaders, Laptops & Netbooks; use item-specifics (brand, model, storage capacity,
  condition) as first-class match fields, not just free-text title search.
- **Craigslist RSS integration**: target the `sya` (electronics) and `mob` (cell
  phones) category feeds per configured metro; parse title against a model/spec keyword list.
- **v0 keyword/category seed list** (client-configurable per `Watch`, but default suggestions
  for onboarding):
  - Brands/models: iPhone (11/12/13/14/15 + Pro/Max variants), Samsung Galaxy S/Note/Fold
    series, Google Pixel, iPad (Air/Pro/Mini), MacBook (Air/Pro, M1/M2/M3), AirPods
    (Pro/Max).
  - Spec tokens to extract for dedup/matching: storage size (64GB/128GB/256GB/512GB/1TB),
    condition ("unlocked", "cracked", "for parts", "like new"), carrier lock status.
  - Categories: eBay `Cell Phones & Smartphones`, `Tablets & eReaders`, `Laptops & Netbooks`;
    Craigslist `electronics` (`sya`) and `cell phones` (`mob`).
- **Dedup/spam filter v0**: normalize on `model + storage + condition-bucket + rounded price`
  across sources — precise model/storage tokens make this far more reliable than for
  furniture or vehicles.
- **Concierge outreach targets**: r/flipping and similar reselling subreddits, phone-flipping
  Discord/Facebook groups, local phone-repair/refurb shops that also resell inventory. Pitch:
  free 2-week trial tied to "first matched listing that beats their current manual search
  time."

## Not chosen (kept for later expansion)

Used car dealers and industrial equipment dealers remain reasonable second verticals once the
eBay/Craigslist pipeline and dedup logic are proven — both have strong eBay category support
and could reuse most of the v0 architecture with a different keyword/spec schema.
