<script setup lang="ts">
import { computed, onMounted, ref } from "vue";

const props = defineProps<{ locale: "ja" | "en"; open: boolean }>();
const emit = defineEmits<{ toggle: [open: boolean] }>();
// A fixed, anonymous campaign marker, never an email, device ID or user-provided URL.
const handoffUrl = "https://www.maipilot.jp/?from=mobile_handoff#download";
const canShare = ref(false);
const busy = ref(false);
const status = ref<"" | "copied" | "shared" | "manual" | "shareFailed">("");
const showManual = ref(false);
const copyField = ref<HTMLInputElement | null>(null);
const messages = {
  ja: {
    title: "スマホで見ている方へ：Windows PCで開く",
    body: "MaiPilotはWindows 10/11用です。リンクをPCに送って、このページから無料版をダウンロードしてください。",
    copy: "PC用リンクをコピー", share: "リンクを共有", link: "Windows PCで開くリンク",
    copied: "リンクをコピーしました。Windows PCで開いてください。",
    shared: "共有メニューにリンクを渡しました。Windows PCで開いてください。",
    manual: "コピーを許可できませんでした。下のリンクを選択して手動でコピーしてください。",
    shareFailed: "共有できませんでした。リンクをコピーしてご利用ください。",
    note: "メールアドレスの入力は不要です。スマホにアプリをインストールする操作ではありません。",
  },
  en: {
    title: "On your phone? Open this on a Windows PC",
    body: "MaiPilot runs on Windows 10/11. Send this link to your PC, then download the free installer from this page.",
    copy: "Copy link for PC", share: "Share link", link: "Link to open on your Windows PC",
    copied: "Link copied. Open it on your Windows PC.",
    shared: "Link passed to the share menu. Open it on your Windows PC.",
    manual: "Clipboard access was unavailable. Select and copy the link below manually.",
    shareFailed: "Sharing was unavailable. Copy the link instead.",
    note: "No email address needed. This does not install the app on your phone.",
  },
} as const;
const t = computed(() => messages[props.locale]);
const report = (name: "pc_link_copy" | "pc_link_share") => {
  const win = window as typeof window & {
    MaiPilotAnalytics?: { track: (event: string, parameters: { cta_location: string }) => void };
  };
  win.MaiPilotAnalytics?.track(name, { cta_location: "hero" });
};
onMounted(() => { canShare.value = typeof navigator.share === "function"; });
const copyLink = async () => {
  if (busy.value) return;
  busy.value = true;
  status.value = "";
  try {
    if (!navigator.clipboard?.writeText) throw new Error("Clipboard unavailable");
    await navigator.clipboard.writeText(handoffUrl);
    status.value = "copied";
    showManual.value = false;
    report("pc_link_copy");
  } catch {
    showManual.value = true;
    status.value = "manual";
  } finally { busy.value = false; }
};
const shareLink = async () => {
  if (busy.value || !canShare.value) return;
  busy.value = true;
  status.value = "";
  try {
    await navigator.share({ title: "MaiPilot", url: handoffUrl });
    status.value = "shared";
    report("pc_link_share");
  } catch (error) {
    // Dismissing the native share sheet is not a completed handoff.
    if (!(error instanceof Error && error.name === "AbortError")) status.value = "shareFailed";
  } finally { busy.value = false; }
};
const onToggle = (event: Event) => emit("toggle", (event.target as HTMLDetailsElement).open);
</script>

<template>
  <details class="pc-handoff" :open="open" @toggle="onToggle">
    <summary>{{ t.title }}</summary>
    <div class="pc-handoff-body">
      <p>{{ t.body }}</p>
      <div class="pc-handoff-actions">
        <button class="btn ghost" type="button" :disabled="busy" @click="copyLink">{{ t.copy }}</button>
        <button v-if="canShare" class="btn ghost" type="button" :disabled="busy" @click="shareLink">{{ t.share }}</button>
      </div>
      <p class="handoff-status" role="status" aria-live="polite">{{ status ? t[status] : "" }}</p>
      <label v-if="showManual" class="handoff-manual">
        {{ t.link }}
        <input ref="copyField" :value="handoffUrl" readonly type="url" @focus="copyField?.select()" @click="copyField?.select()" />
      </label>
      <small>{{ t.note }}</small>
    </div>
  </details>
</template>
