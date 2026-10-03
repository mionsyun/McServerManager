<script setup lang="ts">
import { ref, watch } from "vue";

const props = defineProps<{
  src: string | null;
  title: string;
  caption: string;
  alt: string;
  pendingLabel: string;
  pendingNote: string;
  sourceNote?: string;
  eager?: boolean;
}>();

const failed = ref(false);
watch(() => props.src, () => { failed.value = false; });
</script>

<template>
  <figure class="product-shot">
    <div class="product-shot-media">
      <a v-if="src && !failed" :href="src" target="_blank" rel="noopener noreferrer">
        <img :src="src" :alt="alt" :loading="eager ? 'eager' : 'lazy'"
          :fetchpriority="eager ? 'high' : 'auto'" decoding="async" @error="failed = true" />
      </a>
      <div v-else class="product-shot-pending">
        <span class="product-shot-brand" aria-hidden="true">MaiPilot</span>
        <strong>{{ pendingLabel }}</strong>
        <p>{{ pendingNote }}</p>
      </div>
    </div>
    <figcaption>
      <h3>{{ title }}</h3>
      <p>{{ caption }}</p>
      <small v-if="src && !failed && sourceNote" class="product-shot-source">{{ sourceNote }}</small>
    </figcaption>
  </figure>
</template>
