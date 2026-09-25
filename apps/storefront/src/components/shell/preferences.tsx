"use client";

import { usePathname } from "next/navigation";
import { type FormEvent, useState } from "react";
import { GlobeIcon } from "@/components/icons";
import { useLocaleContext } from "@/components/providers/locale-provider";
import {
  ACCOUNT_LOCALE_COOKIE, chargeableCurrencies, intlLocale, isLocale, LOCALE_COOKIE, localeNames, locales, localizePath,
  PREFERENCE_MAX_AGE, REGION_COOKIE, regions, serializeRegion, stripLocale,
} from "@/i18n/config";

function writeCookie(name: string, value: string) {
  const secure = window.location.protocol === "https:" ? "; secure" : "";
  document.cookie = `${name}=${encodeURIComponent(value)}; path=/; max-age=${PREFERENCE_MAX_AGE}; samesite=lax${secure}`;
}

const hasCookie = (name: string) => document.cookie.split("; ").some((c) => c.startsWith(`${name}=`));

/** "Pakistan · USD · English". Ship-to, currency and language are chosen independently. */
export function PreferencesControl() {
  const { locale, region, regionDetected, dictionary: t } = useLocaleContext();
  const pathname = usePathname();
  const [country, setCountry] = useState(region.country);
  const regionName = (code: string) => new Intl.DisplayNames([intlLocale[locale]], { type: "region" }).of(code) ?? code;
  // Only currencies checkout can charge are offered; anything else would show prices we cannot honour.
  const currency = (chargeableCurrencies as readonly string[]).includes(region.currency) ? region.currency : chargeableCurrencies[0];

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const nextLocale = String(data.get("language"));
    if (!isLocale(nextLocale)) return;
    writeCookie(REGION_COOKIE, serializeRegion({ country: String(data.get("country")), currency: String(data.get("currency")) }));
    writeCookie(LOCALE_COOKIE, nextLocale);
    // ponytail: the account preference is mirrored in a cookie; write it to the profile once an account preferences API exists.
    if (hasCookie(ACCOUNT_LOCALE_COOKIE)) writeCookie(ACCOUNT_LOCALE_COOKIE, nextLocale);
    // A full load, not a client transition: language changes the root layout (lang, dir, fonts).
    window.location.assign(localizePath(nextLocale, stripLocale(pathname)) + window.location.search);
  }

  return (
    <>
      <button type="button" className="header-action" popoverTarget="preferences-panel" aria-label={`${t.preferences.title}: ${regionName(region.country)}, ${currency}, ${localeNames[locale]}`}>
        <GlobeIcon /><span className="header-action__label">{locale.toUpperCase()} · {currency}</span>
      </button>
      <div id="preferences-panel" popover="auto" className="header-popover header-popover--preferences">
        <form onSubmit={save} className="preferences">
          <p className="t-h3">{t.preferences.title}</p>
          <p className="t-meta">{regionName(region.country)} · {currency} · {localeNames[locale]}</p>
          {regionDetected ? <p className="t-meta">{t.preferences.detectedHint}</p> : null}
          <label className="field">
            <span>{t.preferences.shipTo}</span>
            <select name="country" value={country} onChange={(event) => setCountry(event.target.value)}>
              {Object.keys(regions).sort((a, b) => regionName(a).localeCompare(regionName(b), intlLocale[locale])).map((code) => <option key={code} value={code}>{regionName(code)}</option>)}
            </select>
          </label>
          <label className="field">
            <span>{t.preferences.currency}</span>
            <select name="currency" defaultValue={currency}>
              {chargeableCurrencies.map((code) => <option key={code} value={code}>{code} · {new Intl.DisplayNames([intlLocale[locale]], { type: "currency" }).of(code)}</option>)}
            </select>
            <small>{t.preferences.currencyNote}</small>
          </label>
          <label className="field">
            <span>{t.preferences.language}</span>
            <select name="language" defaultValue={locale}>
              {locales.map((code) => <option key={code} value={code} lang={code}>{localeNames[code]}</option>)}
            </select>
          </label>
          <button type="submit" className="button button--primary button--medium">{t.preferences.save}</button>
        </form>
      </div>
    </>
  );
}
