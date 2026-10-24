# Smart Import Shopping — UI reference captures

Private design reference only (do not copy logos, text or product photos into the product).

## Brand tokens
- Background #F7F4EE, text #191B1A, accent #176B4D
- Headings: Fraunces · Body: Inter
- 8 px spacing grid, 24 px layout grid

## Captures (22 of 32 succeeded)

| File | Site | UI pattern | Viewport | Result |
|---|---|---|---|---|
| ssense_plp_desktop | SSENSE | Product listing — filters & typography | Desktop 1920×1080 | OK — Full-page shot clipped to the top 16000px of 18138px. |
| ssense_plp_mobile | SSENSE | Product listing — filters & typography | Mobile 390×844 | OK — Full-page shot clipped to the top 16000px of 20973px. |
| ssense_pdp_desktop | SSENSE | Product page | Desktop 1920×1080 | OK — First product opened from https://www.ssense.com/en-us/men/clothing. |
| ssense_pdp_mobile | SSENSE | Product page | Mobile 390×844 | OK — First product opened from https://www.ssense.com/en-us/men/clothing. |
| ssense_sizeguide_desktop | SSENSE | Size guide panel | Desktop 1920×1080 | OK — First product opened from https://www.ssense.com/en-us/men/clothing. Panel opened via "SIZE GUIDE". |
| ssense_sizeguide_mobile | SSENSE | Size guide panel | Mobile 390×844 | OK — First product opened from https://www.ssense.com/en-us/men/clothing. Panel opened via "SIZE GUIDE". |
| mytheresa_landing_desktop | Mytheresa | Men's landing — editorial layout | Desktop 1920×1080 | OK |
| mytheresa_landing_mobile | Mytheresa | Men's landing — editorial layout | Mobile 390×844 | OK — A "Go to App" bottom sheet covers the lower half of the viewport shot; the script could not find its close button. |
| mytheresa_pdp_desktop | Mytheresa | Product page — delivery & duties | Desktop 1920×1080 | OK — First product opened from https://www.mytheresa.com/us/en/men. Delivery section opened via "Delivery & Free Returns". |
| mytheresa_sizeguide_desktop | Mytheresa | Size guide panel | Desktop 1920×1080 | OK — First product opened from https://www.mytheresa.com/us/en/men. Panel opened via "Size Guide". |
| end_plp_desktop | END. Clothing | Product listing — filters & typography | Desktop 1920×1080 | OK |
| end_plp_mobile | END. Clothing | Product listing — filters & typography | Mobile 390×844 | OK |
| end_pdp_desktop | END. Clothing | Product page | Desktop 1920×1080 | OK — First product opened from https://www.endclothing.com/en-us/collections/apparel. |
| end_pdp_mobile | END. Clothing | Product page | Mobile 390×844 | OK — First product opened from https://www.endclothing.com/en-us/collections/apparel. |
| end_sizeguide_desktop | END. Clothing | Size guide panel | Desktop 1920×1080 | OK — First product opened from https://www.endclothing.com/en-us/collections/apparel. Panel opened via "Size guide". |
| end_sizeguide_mobile | END. Clothing | Size guide panel | Mobile 390×844 | OK — First product opened from https://www.endclothing.com/en-us/collections/apparel. Panel opened via "Size guide". |
| dribbble_minimalist-fashion-ui_desktop | Dribbble | Search: "minimalist fashion ui" | Desktop 1920×1080 | OK |
| dribbble_minimalist-fashion-ui_mobile | Dribbble | Search: "minimalist fashion ui" | Mobile 390×844 | OK — Full-page shot clipped to the top 16000px of 16359px. |
| dribbble_wardrobe-app_desktop | Dribbble | Search: "wardrobe app" | Desktop 1920×1080 | OK |
| dribbble_wardrobe-app_mobile | Dribbble | Search: "wardrobe app" | Mobile 390×844 | OK |
| behance_wardrobe-tracker-app_desktop | Behance | Search: "wardrobe tracker app" | Desktop 1920×1080 | OK |
| behance_wardrobe-tracker-app_mobile | Behance | Search: "wardrobe tracker app" | Mobile 390×844 | OK |

## Failed
- mytheresa_pdp_mobile: Site returned an error page ("Something went wrong") instead of content
- mytheresa_sizeguide_mobile: Site returned an error page ("Something went wrong") instead of content
- farfetch_pdp_desktop: Blocked by bot protection (page title "Access Denied"); not solved within 5 min
- farfetch_pdp_mobile: Blocked by bot protection (page title "Access Denied"); not solved within 5 min
- farfetch_sizeguide_desktop: Blocked by bot protection (page title "Access Denied"); not solved within 5 min
- farfetch_sizeguide_mobile: Blocked by bot protection (page title "Access Denied"); not solved within 5 min
- netaporter_pdp_desktop: Blocked by bot protection (page title "Access Denied"); not solved within 5 min
- netaporter_pdp_mobile: Blocked by bot protection (page title "Access Denied"); not solved within 5 min
- netaporter_sizeguide_desktop: Blocked by bot protection (page title "Access Denied"); not solved within 5 min
- netaporter_sizeguide_mobile: Blocked by bot protection (page title "Access Denied"); not solved within 5 min

## Key takeaways
- **Listing & filters:** SSENSE (left filter sidebar, uppercase type), END. (Show filters button, grid density toggle).
- **Product page:** SSENSE (three-column: details / image / buy box), END. (accordions: Description, Sizing, Shipping, Returns).
- **Duties & delivery:** Mytheresa desktop shows "Incl. duties, excl. taxes and shipping costs. No extra import fees on delivery" under the price, plus a Delivery & Free Returns accordion. SSENSE shows "Duties calculated at checkout".
- **Size guide:** SSENSE (garment diagram + per-size measurements + inch/cm toggle + size conversion tab), Mytheresa (cross-region size conversion table), END. (simple measurement table; modal on desktop, bottom sheet on mobile).
- **App concepts:** Dribbble "minimalist fashion ui" / "wardrobe app", Behance "wardrobe tracker app".

## Prompt to use with these files
These are screenshots of luxury-fashion sites and design galleries I collected as private UI reference for my app "Smart Import Shopping" (an app for buying fashion from abroad with duties, delivery and sizing made clear). Using my brand tokens above, analyse the patterns (layout, typography, filters, price + duties messaging, size guide) and propose screens for my app: listing, product page, size guide sheet and checkout duty summary. Describe patterns in your own words; do not reproduce any logos, text or product photos from the screenshots.
